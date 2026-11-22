using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Application.Common;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Authors;

/// <summary>Moderator-only cleanup of duplicated author records.</summary>
public interface IAuthorDeduplicationService
{
    /// <summary>Groups of authors with the same normalized name (≥ 2 records), ordered by name.</summary>
    Task<IReadOnlyList<AuthorDuplicateGroupDto>> GetDuplicatesAsync(CancellationToken ct);

    /// <summary>Merges the source authors into the target in a single transaction (docs/api-contracts.md v1.11).</summary>
    Task<AuthorMergeResultDto> MergeAsync(MergeAuthorsRequest request, CancellationToken ct);
}

public sealed class AuthorDeduplicationService(IAppDbContext db, ICurrentUser currentUser, IClock clock) : IAuthorDeduplicationService
{
    public const int MaxSources = 50;

    public async Task<IReadOnlyList<AuthorDuplicateGroupDto>> GetDuplicatesAsync(CancellationToken ct)
    {
        await EnsureModeratorAsync(ct);

        var authors = await db.Authors.AsNoTracking()
            .Where(a => db.Authors.Any(o => o.NormalizedName == a.NormalizedName && o.Id != a.Id))
            .Select(a => new { a.Id, a.Name, a.NormalizedName, a.CreatedAt })
            .ToListAsync(ct);
        if (authors.Count == 0)
        {
            return [];
        }

        var ids = authors.Select(a => a.Id).ToList();
        var bookCounts = (await db.BookAuthors.AsNoTracking()
                .Where(ba => ids.Contains(ba.AuthorId))
                .GroupBy(ba => ba.AuthorId)
                .Select(g => new { AuthorId = g.Key, Count = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.AuthorId, x => x.Count);
        var isosByAuthor = (await db.AuthorCountries.AsNoTracking()
                .Where(ac => ac.RelationshipType == AuthorCountryRelationship.Citizenship && ids.Contains(ac.AuthorId))
                .Select(ac => new { ac.AuthorId, ac.Country!.IsoCode })
                .ToListAsync(ct))
            .GroupBy(x => x.AuthorId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.IsoCode).OrderBy(i => i, StringComparer.Ordinal).ToList());

        return authors
            .GroupBy(a => a.NormalizedName, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new AuthorDuplicateGroupDto(
                g.Key,
                g.Select(a => new AuthorDuplicateDto(
                        a.Id, a.Name, bookCounts.GetValueOrDefault(a.Id), isosByAuthor.GetValueOrDefault(a.Id) ?? [], a.CreatedAt))
                    .OrderByDescending(a => a.BookCount)
                    .ThenBy(a => a.CreatedAt)
                    .ThenBy(a => a.Id)
                    .ToList()))
            .ToList();
    }

    public async Task<AuthorMergeResultDto> MergeAsync(MergeAuthorsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureModeratorAsync(ct);
        Validate(request);

        var sourceIds = request.SourceIds.ToList();
        var allIds = sourceIds.Append(request.TargetId).ToList();
        var loaded = await db.Authors
            .Include(a => a.Books)
            .Include(a => a.Countries)
            .Include(a => a.ExternalIdentifiers)
            .Where(a => allIds.Contains(a.Id))
            .ToListAsync(ct);
        var byId = loaded.ToDictionary(a => a.Id);
        if (allIds.Any(id => !byId.ContainsKey(id)))
        {
            throw new NotFoundException("author.not_found", "Autor não encontrado.");
        }

        var now = clock.UtcNow;
        var target = byId[request.TargetId];
        int relinked = 0, alreadyLinked = 0, countriesAdded = 0;

        // Everything below only stages changes in the tracker; the single SaveChanges at the end is one database
        // transaction (EF wraps one SaveChanges in a transaction), so a failure leaves the catalog untouched.
        var targetBooks = target.Books.Select(b => b.BookId).ToHashSet();
        var targetCountries = target.Countries.ToDictionary(c => (c.CountryId, c.RelationshipType));
        var targetProviders = target.ExternalIdentifiers.Select(i => i.Provider).ToHashSet();

        foreach (var source in sourceIds.Select(id => byId[id]))
        {
            // (1) book links: BookAuthor's key is (book, author), so a "move" is delete + insert with the same position.
            foreach (var link in source.Books.ToList())
            {
                db.BookAuthors.Remove(link);
                if (targetBooks.Add(link.BookId))
                {
                    db.BookAuthors.Add(new BookAuthor(link.BookId, target.Id, link.Position));
                    relinked++;
                }
                else
                {
                    alreadyLinked++;
                }
            }

            // (2) countries: union by (country, relationship); on a key conflict the target row stays unless the source's is better.
            foreach (var relation in source.Countries.ToList())
            {
                db.AuthorCountries.Remove(relation);
                if (targetCountries.TryGetValue((relation.CountryId, relation.RelationshipType), out var existing))
                {
                    existing.AdoptIfBetter(relation, now);
                    continue;
                }

                var copy = relation.CopyFor(target.Id);
                db.AuthorCountries.Add(copy);
                targetCountries[(copy.CountryId, copy.RelationshipType)] = copy;
                countriesAdded++;
            }

            // (3) external ids: one per provider on the target; the others are dropped with the source.
            foreach (var identifier in source.ExternalIdentifiers.ToList())
            {
                if (targetProviders.Add(identifier.Provider))
                {
                    // detach from the source collection first so deleting the source can never cascade to it
                    source.ExternalIdentifiers.Remove(identifier);
                    identifier.MoveTo(target.Id);
                    target.ExternalIdentifiers.Add(identifier);
                }
                else
                {
                    db.ExternalAuthorIdentifiers.Remove(identifier);
                }
            }

            // (4) birth date only fills a gap; (5) the target's name is kept.
            target.BirthDate ??= source.BirthDate;
            db.Authors.Remove(source);
        }

        target.Touch(now);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.DiscardChanges();
            throw;
        }

        return new AuthorMergeResultDto(target.Id, sourceIds.Count, relinked, alreadyLinked, countriesAdded);
    }

    private static void Validate(MergeAuthorsRequest request)
    {
        var errors = new ValidationErrors();
        var sources = request.SourceIds ?? [];
        if (request.TargetId == Guid.Empty)
        {
            errors.Add("targetId", "Informe o autor de destino.");
        }

        if (sources.Count == 0)
        {
            errors.Add("sourceIds", "Informe ao menos um autor de origem.");
        }
        else if (sources.Count > MaxSources)
        {
            errors.Add("sourceIds", $"Informe no máximo {MaxSources} autores de origem.");
        }
        else if (sources.Distinct().Count() != sources.Count)
        {
            errors.Add("sourceIds", "Os autores de origem não podem se repetir.");
        }
        else if (sources.Contains(request.TargetId))
        {
            errors.Add("sourceIds", "O autor de destino não pode estar entre os de origem.");
        }
        else if (sources.Contains(Guid.Empty))
        {
            errors.Add("sourceIds", "Identificador de autor inválido.");
        }

        errors.ThrowIfAny();
    }

    private async Task EnsureModeratorAsync(CancellationToken ct)
    {
        var userId = currentUser.GetRequiredUserId();
        if (!await GeoEditPolicy.IsModeratorAsync(db, userId, ct))
        {
            throw new ForbiddenException("author.moderator_required", "Apenas moderadores podem gerenciar autores duplicados.");
        }
    }
}
