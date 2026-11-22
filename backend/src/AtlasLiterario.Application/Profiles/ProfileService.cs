using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Application.Common;
using AtlasLiterario.Application.Maps;
using AtlasLiterario.Application.Statistics;
using AtlasLiterario.Domain.Common;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Profiles;

/// <summary>
/// Public profiles, follows and the owner's maps (docs/api-contracts.md §8.1). Every endpoint requires authentication.
/// A profile is visible to others only when its owner chose <see cref="MapVisibility.Public"/>; otherwise the answer is the same
/// <c>profile.not_found</c> as for an unknown handle. Maps always use the owner's id and only Read books, through the same
/// loader/aggregator as the private maps. Nothing here returns e-mail, internal user ids or library item ids.
/// </summary>
public sealed class ProfileService(IAppDbContext db, ICurrentUser currentUser, IClock clock) : IProfileService
{
    public const int MinQueryLength = 2;
    public const int DefaultLimit = 10;
    public const int MaxLimit = 20;

    private sealed record Target(Guid Id, string Name, string Handle, DateTimeOffset CreatedAt, MapVisibility Visibility);

    private sealed record Row(Guid Id, string Handle, string Name);

    public async Task<IReadOnlyList<ProfileSummaryDto>> SearchAsync(string? q, int? limit, CancellationToken ct)
    {
        var viewerId = currentUser.GetRequiredUserId();
        var errors = new ValidationErrors();
        var term = (q ?? string.Empty).Trim();
        if (term.Length < MinQueryLength)
        {
            errors.Add("q", $"Informe ao menos {MinQueryLength} caracteres.");
        }

        var take = limit ?? DefaultLimit;
        if (take is < 1 or > MaxLimit)
        {
            errors.Add("limit", $"O limite deve estar entre 1 e {MaxLimit}.");
        }

        errors.ThrowIfAny();

        var normalized = TextNormalizer.NormalizeForComparison(term);
        if (normalized.Length == 0)
        {
            return [];
        }

        var escaped = EscapeLike(normalized);
        var prefix = escaped + "%";
        var wordPrefix = "% " + escaped + "%";

        var rows = await db.Users.AsNoTracking()
            .Where(u => u.MapVisibility == MapVisibility.Public && u.Handle != null && u.Id != viewerId)
            .Where(u => EF.Functions.Like(u.Handle!, prefix, "\\")
                        || EF.Functions.Like(u.NormalizedName, prefix, "\\")
                        || EF.Functions.Like(u.NormalizedName, wordPrefix, "\\"))
            .OrderBy(u => u.Handle == normalized ? 0 : EF.Functions.Like(u.Handle!, prefix, "\\") ? 1 : 2)
            .ThenBy(u => u.NormalizedName)
            .ThenBy(u => u.Id)
            .Take(take)
            .Select(u => new Row(u.Id, u.Handle!, u.Name))
            .ToListAsync(ct);

        return await SummariesAsync(viewerId, rows, ct);
    }

    public async Task<ProfileDto> GetAsync(string handle, CancellationToken ct)
    {
        var viewerId = currentUser.GetRequiredUserId();
        var target = await ResolveAsync(handle, viewerId, ct);
        var isSelf = target.Id == viewerId;

        var followers = await db.UserFollows.CountAsync(f => f.FollowedId == target.Id, ct);
        var following = await db.UserFollows.CountAsync(f => f.FollowerId == target.Id, ct);
        var (authors, origins, input) = await StatisticsService.LoadMapsAsync(db, target.Id, ct);
        var isFollowing = !isSelf && await db.UserFollows.AnyAsync(f => f.FollowerId == viewerId && f.FollowedId == target.Id, ct);
        var followsYou = !isSelf && await db.UserFollows.AnyAsync(f => f.FollowerId == target.Id && f.FollowedId == viewerId, ct);

        return new ProfileDto(
            target.Handle, target.Name, followers, input.LibraryBooks.Count, isFollowing, followsYou, following,
            authors.ExploredCountries, origins.ExploredCountries, target.CreatedAt, isSelf);
    }

    public async Task FollowAsync(string handle, CancellationToken ct)
    {
        var viewerId = currentUser.GetRequiredUserId();
        var target = await FindByHandleAsync(handle, ct) ?? throw ProfileNotFound();
        if (target.Id == viewerId)
        {
            throw new ValidationException("follow.self", "Não é possível seguir a si mesmo.");
        }

        if (target.Visibility != MapVisibility.Public)
        {
            throw ProfileNotFound();
        }

        if (await db.UserFollows.AnyAsync(f => f.FollowerId == viewerId && f.FollowedId == target.Id, ct))
        {
            return;
        }

        db.UserFollows.Add(new UserFollow(viewerId, target.Id, clock.UtcNow));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Concurrent identical request hit the composite primary key: following is idempotent.
            db.DiscardChanges();
            if (!await db.UserFollows.AnyAsync(f => f.FollowerId == viewerId && f.FollowedId == target.Id, ct))
            {
                throw;
            }
        }
    }

    public async Task UnfollowAsync(string handle, CancellationToken ct)
    {
        var viewerId = currentUser.GetRequiredUserId();
        var target = await FindByHandleAsync(handle, ct);
        if (target is null)
        {
            return;
        }

        var follow = await db.UserFollows.SingleOrDefaultAsync(f => f.FollowerId == viewerId && f.FollowedId == target.Id, ct);
        if (follow is null)
        {
            return;
        }

        db.UserFollows.Remove(follow);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<ProfileSummaryDto>> GetFollowersAsync(string handle, int? page, int? pageSize, CancellationToken ct)
    {
        var viewerId = currentUser.GetRequiredUserId();
        var target = await ResolveAsync(handle, viewerId, ct);
        return await FollowersPageAsync(viewerId, target.Id, page, pageSize, ct);
    }

    public async Task<PagedResult<ProfileSummaryDto>> GetFollowingAsync(string handle, int? page, int? pageSize, CancellationToken ct)
    {
        var viewerId = currentUser.GetRequiredUserId();
        var target = await ResolveAsync(handle, viewerId, ct);
        return await FollowingPageAsync(viewerId, target.Id, page, pageSize, ct);
    }

    public Task<PagedResult<ProfileSummaryDto>> GetMyFollowersAsync(int? page, int? pageSize, CancellationToken ct)
    {
        var viewerId = currentUser.GetRequiredUserId();
        return FollowersPageAsync(viewerId, viewerId, page, pageSize, ct);
    }

    public Task<PagedResult<ProfileSummaryDto>> GetMyFollowingAsync(int? page, int? pageSize, CancellationToken ct)
    {
        var viewerId = currentUser.GetRequiredUserId();
        return FollowingPageAsync(viewerId, viewerId, page, pageSize, ct);
    }

    public async Task<MapDataDto> GetMapAsync(string handle, MapType mapType, CancellationToken ct)
    {
        var target = await ResolveAsync(handle, currentUser.GetRequiredUserId(), ct);
        return await UserMapReader.GetMapAsync(db, target.Id, mapType, MapStatusFilter.Read, ct);
    }

    public async Task<ProfileCountryDetailDto> GetCountryDetailAsync(string handle, MapType mapType, string iso, CancellationToken ct)
    {
        var target = await ResolveAsync(handle, currentUser.GetRequiredUserId(), ct);
        var detail = await UserMapReader.GetCountryDetailAsync(db, target.Id, mapType, iso, MapStatusFilter.Read, ct);

        // Reviews of the owner's Read books only (the ids come from the owner's Read set; re-filtered here as a safeguard).
        var itemIds = detail.Books.Select(b => b.LibraryItemId).ToList();
        var reviews = itemIds.Count == 0
            ? []
            : await db.UserBooks.AsNoTracking()
                .Where(ub => ub.UserId == target.Id && ub.Status == ReadingStatus.Read && itemIds.Contains(ub.Id))
                .Select(ub => new { ub.Id, ub.Review })
                .ToDictionaryAsync(x => x.Id, x => x.Review, ct);

        ProfileBookDto ToBook(CountryBookDto b) => new(
            b.BookId, b.Title, b.CoverUrl, b.Authors, b.FinishedDate, b.Rating,
            string.IsNullOrWhiteSpace(reviews.GetValueOrDefault(b.LibraryItemId)) ? null : reviews[b.LibraryItemId]);

        return new ProfileCountryDetailDto(
            detail.MapType, detail.Iso, detail.Name, detail.Continent, detail.BookCount, detail.AuthorCount,
            detail.Books.Select(ToBook).ToList(),
            detail.Authors
                .Select(a => new ProfileCountryAuthorDto(
                    a.AuthorId, a.Name, a.CountryIso, a.CountryName, a.Books.Select(ToBook).ToList()))
                .ToList(),
            nameof(MapStatusFilter.Read));
    }

    private async Task<PagedResult<ProfileSummaryDto>> FollowersPageAsync(
        Guid viewerId, Guid ownerId, int? page, int? pageSize, CancellationToken ct)
    {
        var query = db.UserFollows.AsNoTracking()
            .Where(f => f.FollowedId == ownerId)
            .Join(db.Users, f => f.FollowerId, u => u.Id, (f, u) => new { f.CreatedAt, u.Id, u.Handle, u.Name, u.MapVisibility })
            .Where(x => x.MapVisibility == MapVisibility.Public && x.Handle != null);
        return await PageAsync(viewerId, query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id),
            x => new Row(x.Id, x.Handle!, x.Name), page, pageSize, ct);
    }

    private async Task<PagedResult<ProfileSummaryDto>> FollowingPageAsync(
        Guid viewerId, Guid ownerId, int? page, int? pageSize, CancellationToken ct)
    {
        var query = db.UserFollows.AsNoTracking()
            .Where(f => f.FollowerId == ownerId)
            .Join(db.Users, f => f.FollowedId, u => u.Id, (f, u) => new { f.CreatedAt, u.Id, u.Handle, u.Name, u.MapVisibility })
            .Where(x => x.MapVisibility == MapVisibility.Public && x.Handle != null);
        return await PageAsync(viewerId, query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id),
            x => new Row(x.Id, x.Handle!, x.Name), page, pageSize, ct);
    }

    private async Task<PagedResult<ProfileSummaryDto>> PageAsync<T>(
        Guid viewerId, IOrderedQueryable<T> ordered, System.Linq.Expressions.Expression<Func<T, Row>> toRow,
        int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await ordered.CountAsync(ct);
        var rows = await ordered.Skip((p - 1) * size).Take(size).Select(toRow).ToListAsync(ct);
        return PagedResult<ProfileSummaryDto>.Create(await SummariesAsync(viewerId, rows, ct), p, size, total);
    }

    /// <summary>Counts and viewer-relative flags for a page of profiles in four batched queries (no N+1).</summary>
    private async Task<List<ProfileSummaryDto>> SummariesAsync(Guid viewerId, List<Row> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Id).ToList();
        var followers = await db.UserFollows.AsNoTracking()
            .Where(f => ids.Contains(f.FollowedId))
            .GroupBy(f => f.FollowedId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var readBooks = await db.UserBooks.AsNoTracking()
            .Where(ub => ids.Contains(ub.UserId) && ub.Status == ReadingStatus.Read)
            .GroupBy(ub => ub.UserId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var following = (await db.UserFollows.AsNoTracking()
            .Where(f => f.FollowerId == viewerId && ids.Contains(f.FollowedId))
            .Select(f => f.FollowedId)
            .ToListAsync(ct)).ToHashSet();
        var followsYou = (await db.UserFollows.AsNoTracking()
            .Where(f => f.FollowedId == viewerId && ids.Contains(f.FollowerId))
            .Select(f => f.FollowerId)
            .ToListAsync(ct)).ToHashSet();

        return rows
            .Select(r => new ProfileSummaryDto(
                r.Handle, r.Name, followers.GetValueOrDefault(r.Id), readBooks.GetValueOrDefault(r.Id),
                following.Contains(r.Id), followsYou.Contains(r.Id)))
            .ToList();
    }

    private Task<Target?> FindByHandleAsync(string handle, CancellationToken ct)
    {
        var normalized = User.NormalizeHandle(handle) ?? string.Empty;
        return db.Users.AsNoTracking()
            .Where(u => u.Handle == normalized)
            .Select(u => new Target(u.Id, u.Name, u.Handle!, u.CreatedAt, u.MapVisibility))
            .SingleOrDefaultAsync(ct);
    }

    /// <summary>The target when it exists and is Public (or is the caller); one identical 404 otherwise.</summary>
    private async Task<Target> ResolveAsync(string handle, Guid viewerId, CancellationToken ct)
    {
        var target = await FindByHandleAsync(handle, ct);
        if (target is null || (target.Visibility != MapVisibility.Public && target.Id != viewerId))
        {
            throw ProfileNotFound();
        }

        return target;
    }

    private static NotFoundException ProfileNotFound() =>
        new("profile.not_found", "Perfil não encontrado.");

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
