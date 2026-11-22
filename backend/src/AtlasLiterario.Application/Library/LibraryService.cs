using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Application.Books;
using AtlasLiterario.Application.Common;
using AtlasLiterario.Domain.Common;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtlasLiterario.Application.Library;

/// <summary>Query of GET /api/library (docs/api-contracts.md §4).</summary>
public sealed record LibraryQuery(
    string? Q,
    IReadOnlyList<ReadingStatus>? Status,
    string? CountryIso,
    string? MapType,
    string? Genre,
    int? MinRating,
    string? Sort,
    string? Order,
    int? Page,
    int? PageSize);

public interface ILibraryService
{
    Task<PagedResult<LibraryItemDto>> ListAsync(LibraryQuery query, CancellationToken ct);

    Task<LibraryItemDto> GetAsync(Guid id, CancellationToken ct);

    Task<LibraryItemDto> AddAsync(AddToLibraryRequest request, CancellationToken ct);

    Task<LibraryItemDto> UpdateAsync(Guid id, UpdateLibraryItemRequest request, CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);
}

/// <summary>The user's reading experience. Other users' items are reported as 404 (existence is not revealed).</summary>
public sealed partial class LibraryService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IBookImportService importService,
    IClock clock,
    ILogger<LibraryService> logger) : ILibraryService
{
    private static readonly string[] SortValues = ["title", "author", "createdAt", "finishedDate", "rating", "publicationYear"];

    public async Task<PagedResult<LibraryItemDto>> ListAsync(LibraryQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var userId = currentUser.GetRequiredUserId();
        var (sort, descending, mapType, countryIso) = ValidateQuery(query);
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);

        var items = db.UserBooks.AsNoTracking().Where(ub => ub.UserId == userId);

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var lower = query.Q.Trim().ToLowerInvariant();
            var normalized = TextNormalizer.NormalizeForComparison(query.Q);
            items = items.Where(ub => ub.Book!.Title.ToLower().Contains(lower)
                                      || ub.Book.Authors.Any(ba => ba.Author!.NormalizedName.Contains(normalized)));
        }

        if (query.Status is { Count: > 0 })
        {
            var statuses = query.Status.Distinct().ToList();
            items = items.Where(ub => statuses.Contains(ub.Status));
        }

        if (countryIso is not null)
        {
            items = mapType switch
            {
                MapType.Authors => items.Where(ub => ub.Book!.Authors.Any(ba => ba.Author!.Countries.Any(c =>
                    c.RelationshipType == AuthorCountryRelationship.Citizenship && c.Country!.IsoCode == countryIso))),
                MapType.Origins => items.Where(ub => ub.Book!.OriginCountries.Any(o => o.Country!.IsoCode == countryIso)),
                _ => items.Where(ub =>
                    ub.Book!.OriginCountries.Any(o => o.Country!.IsoCode == countryIso)
                    || ub.Book.Authors.Any(ba => ba.Author!.Countries.Any(c =>
                        c.RelationshipType == AuthorCountryRelationship.Citizenship && c.Country!.IsoCode == countryIso))),
            };
        }

        if (!string.IsNullOrWhiteSpace(query.Genre))
        {
            var genre = query.Genre.Trim();
            items = items.Where(ub => ub.Book!.Genres.Contains(genre));
        }

        if (query.MinRating is not null)
        {
            var minRating = (short)query.MinRating.Value;
            items = items.Where(ub => ub.Rating != null && ub.Rating >= minRating);
        }

        var total = await items.CountAsync(ct);
        var pageItems = await Sort(items, sort, descending)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .IncludeBookDetails()
            .ToListAsync(ct);

        return PagedResult<LibraryItemDto>.Create(pageItems.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<LibraryItemDto> GetAsync(Guid id, CancellationToken ct) =>
        ToDto(await LoadOwnedAsync(id, tracking: false, ct));

    public async Task<LibraryItemDto> AddAsync(AddToLibraryRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.GetRequiredUserId();

        var hasBookId = request.BookId is not null && request.BookId != Guid.Empty;
        var hasSource = request.Source is not null;
        if (hasBookId == hasSource)
        {
            new ValidationErrors()
                .Add("bookId", "Informe exatamente um entre bookId e source.")
                .ThrowIfAny("library.invalid_book_reference");
        }

        // Domain rules are validated before any external import happens.
        var now = clock.UtcNow;
        var probe = new UserBook(userId, Guid.Empty, request.Status, request.Rating, request.Review, request.StartDate, request.FinishedDate, now);

        Guid bookId;
        if (hasBookId)
        {
            bookId = request.BookId!.Value;
            if (!await db.Books.AnyAsync(b => b.Id == bookId, ct))
            {
                throw new NotFoundException("book.not_found", "Livro não encontrado no catálogo.");
            }
        }
        else
        {
            bookId = await importService.ImportWithoutEnrichmentAsync(request.Source!, ct);
        }

        var existingItemId = await db.UserBooks.AsNoTracking()
            .Where(ub => ub.UserId == userId && ub.BookId == bookId)
            .Select(ub => (Guid?)ub.Id)
            .FirstOrDefaultAsync(ct);
        if (existingItemId is not null)
        {
            throw Duplicate(existingItemId);
        }

        var userBook = new UserBook(
            userId, bookId, probe.Status, probe.Rating, probe.Review, probe.StartDate, probe.FinishedDate, clock.UtcNow);
        db.UserBooks.Add(userBook);
        try
        {
            // The user's action is committed first and independently of RequestAborted: it must never be lost.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException)
        {
            db.DiscardChanges();
            var raced = await db.UserBooks.AsNoTracking()
                .Where(ub => ub.UserId == userId && ub.BookId == bookId)
                .Select(ub => (Guid?)ub.Id)
                .FirstOrDefaultAsync(CancellationToken.None);
            throw Duplicate(raced);
        }

        // Best-effort enrichment inside the request budget (skipped when the book is already Completed).
        try
        {
            await importService.EnrichIfNeededAsync(bookId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LogEnrichmentFailed(logger, bookId, ex.GetType().Name);
        }

        return await GetAsync(userBook.Id, ct);
    }

    public async Task<LibraryItemDto> UpdateAsync(Guid id, UpdateLibraryItemRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userBook = await LoadOwnedAsync(id, tracking: true, ct);
        userBook.Update(request.Status, request.Rating, request.Review, request.StartDate, request.FinishedDate, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return ToDto(userBook);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var userId = currentUser.GetRequiredUserId();
        var userBook = await db.UserBooks.SingleOrDefaultAsync(ub => ub.Id == id && ub.UserId == userId, ct)
                       ?? throw NotFound();
        db.UserBooks.Remove(userBook); // the catalog book is kept
        await db.SaveChangesAsync(ct);
    }

    private async Task<UserBook> LoadOwnedAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var userId = currentUser.GetRequiredUserId();
        var query = db.UserBooks.Where(ub => ub.Id == id && ub.UserId == userId).IncludeBookDetails();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(ct) ?? throw NotFound();
    }

    private static (string Sort, bool Descending, MapType? MapType, string? CountryIso) ValidateQuery(LibraryQuery query)
    {
        var errors = new ValidationErrors();
        var sort = SortValues.FirstOrDefault(s => string.Equals(s, query.Sort ?? "createdAt", StringComparison.OrdinalIgnoreCase));
        if (sort is null)
        {
            errors.Add("sort", "Ordenação inválida (title, author, createdAt, finishedDate, rating, publicationYear).");
        }

        var order = (query.Order ?? "desc").Trim().ToLowerInvariant();
        if (order is not ("asc" or "desc"))
        {
            errors.Add("order", "Ordem inválida (asc, desc).");
        }

        MapType? mapType = null;
        if (!string.IsNullOrWhiteSpace(query.MapType))
        {
            mapType = query.MapType.Trim().ToLowerInvariant() switch
            {
                "authors" => Domain.Enums.MapType.Authors,
                "origins" => Domain.Enums.MapType.Origins,
                _ => null,
            };
            if (mapType is null)
            {
                errors.Add("mapType", "Mapa inválido (authors, origins).");
            }
        }

        var countryIso = string.IsNullOrWhiteSpace(query.CountryIso) ? null : query.CountryIso.Trim().ToUpperInvariant();
        if (countryIso is { Length: not 3 })
        {
            errors.Add("countryIso", "Use o código ISO 3166-1 alpha-3.");
        }

        if (query.MinRating is < UserBook.MinRating or > UserBook.MaxRating)
        {
            errors.Add("minRating", $"Use um valor entre {UserBook.MinRating} e {UserBook.MaxRating}.");
        }

        if (query.Status?.Any(s => !Enum.IsDefined(s)) == true)
        {
            errors.Add("status", "Status inválido.");
        }

        errors.ThrowIfAny();
        return (sort!, order == "desc", mapType, countryIso);
    }

    private static IQueryable<UserBook> Sort(IQueryable<UserBook> items, string sort, bool descending)
    {
        IOrderedQueryable<UserBook> ordered = sort switch
        {
            "title" => descending ? items.OrderByDescending(ub => ub.Book!.Title) : items.OrderBy(ub => ub.Book!.Title),
            "author" => descending
                ? items.OrderByDescending(ub => ub.Book!.Authors.OrderBy(ba => ba.Position).Select(ba => ba.Author!.NormalizedName).FirstOrDefault())
                : items.OrderBy(ub => ub.Book!.Authors.OrderBy(ba => ba.Position).Select(ba => ba.Author!.NormalizedName).FirstOrDefault()),
            "finishedDate" => descending ? items.OrderByDescending(ub => ub.FinishedDate) : items.OrderBy(ub => ub.FinishedDate),
            "rating" => descending ? items.OrderByDescending(ub => ub.Rating) : items.OrderBy(ub => ub.Rating),
            "publicationYear" => descending
                ? items.OrderByDescending(ub => ub.Book!.PublicationYear)
                : items.OrderBy(ub => ub.Book!.PublicationYear),
            _ => descending ? items.OrderByDescending(ub => ub.CreatedAt) : items.OrderBy(ub => ub.CreatedAt),
        };

        return ordered.ThenBy(ub => ub.Id); // stable pagination
    }

    internal static LibraryItemDto ToDto(UserBook userBook) =>
        new(
            userBook.Id,
            userBook.Status,
            userBook.Rating,
            userBook.Review,
            userBook.StartDate,
            userBook.FinishedDate,
            userBook.CreatedAt,
            userBook.UpdatedAt,
            userBook.Book!.ToDetailsDto());

    private static NotFoundException NotFound() => new("library.not_found", "Item não encontrado na sua biblioteca.");

    private static ConflictException Duplicate(Guid? existingItemId = null) =>
        new("library.duplicate", "Este livro já está na sua biblioteca.",
            existingItemId is null ? null : new Dictionary<string, object?> { ["libraryItemId"] = existingItemId.Value.ToString() });

    [LoggerMessage(Level = LogLevel.Warning, Message = "Enrichment after adding book {BookId} failed: {Reason}")]
    private static partial void LogEnrichmentFailed(ILogger logger, Guid bookId, string reason);
}
