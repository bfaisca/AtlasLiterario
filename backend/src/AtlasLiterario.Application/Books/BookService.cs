using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Application.Common;
using AtlasLiterario.Domain.Common;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Books;

public interface IBookService
{
    Task<BookDetailsDto> GetAsync(Guid id, CancellationToken ct);

    Task<BookDetailsDto> CreateAsync(CreateBookRequest request, CancellationToken ct);

    Task<BookDetailsDto> UpdateAsync(Guid id, CreateBookRequest request, CancellationToken ct);

    Task<BookDetailsDto> UpdateOriginCountriesAsync(Guid id, UpdateOriginCountriesRequest request, CancellationToken ct);

    Task<BookDetailsDto> EnrichAsync(Guid id, CancellationToken ct);
}

/// <summary>
/// Global catalog. Reading is open to authenticated users; corrections (business-rules §2) require the book in the
/// caller's library and record <c>UpdatedByUserId</c>.
/// </summary>
public sealed class BookService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IBookImportService importService,
    IClock clock) : IBookService
{
    public const int MaxAuthors = 20;
    public const int MaxGenres = 10;

    public async Task<BookDetailsDto> GetAsync(Guid id, CancellationToken ct)
    {
        currentUser.GetRequiredUserId();
        var book = await db.Books.AsNoTracking().IncludeDetails().SingleOrDefaultAsync(b => b.Id == id, ct)
                   ?? throw NotFound();
        return book.ToDetailsDto();
    }

    public async Task<BookDetailsDto> CreateAsync(CreateBookRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.GetRequiredUserId();
        var authorNames = ValidateRequest(request);
        var origins = await CountryResolution.ResolveIsosAsync(db, request.OriginCountryIsos, "originCountryIsos", ct);
        var now = clock.UtcNow;

        var book = new Book(request.Title, now);
        ApplyBibliographicData(book, request);
        await ApplyCoverAsync(book, request.CoverUrl, isNew: true, ct);
        await EnsureIsbnAvailableAsync(book, ct);
        db.Books.Add(book);

        await ReplaceAuthorsAsync(book, authorNames, existingAuthors: [], now, ct);
        foreach (var country in origins)
        {
            book.OriginCountries.Add(BookOriginCountry.CreateManual(book.Id, country.Id, userId, now));
        }

        // Nothing to look up externally for a manual record; the status reflects which data is known.
        book.GeoEnrichmentStatus = origins.Count > 0 ? GeoEnrichmentStatus.Partial : GeoEnrichmentStatus.Pending;
        await SaveAsync(ct);
        return await GetAsync(book.Id, ct);
    }

    public async Task<BookDetailsDto> UpdateAsync(Guid id, CreateBookRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.GetRequiredUserId();
        var book = await LoadTrackedAsync(id, ct);
        await LibraryAccess.EnsureUserHasBookAsync(db, userId, id, ct);
        var authorNames = ValidateRequest(request);
        var now = clock.UtcNow;

        book.SetTitle(request.Title);
        ApplyBibliographicData(book, request);
        await ApplyCoverAsync(book, request.CoverUrl, isNew: false, ct);
        await EnsureIsbnAvailableAsync(book, ct);

        var current = book.Authors.OrderBy(ba => ba.Position).Select(ba => ba.Author!).ToList();
        foreach (var link in book.Authors.ToList())
        {
            book.Authors.Remove(link);
            db.BookAuthors.Remove(link);
        }

        await ReplaceAuthorsAsync(book, authorNames, current, now, ct);

        if (request.OriginCountryIsos is not null)
        {
            var origins = await CountryResolution.ResolveIsosAsync(db, request.OriginCountryIsos, "originCountryIsos", ct);
            if (await GeoEditPolicy.CheckOriginsAsync(db, userId, id, origins, ct) == GeoEditDecision.Apply)
            {
                ReplaceOrigins(book, origins, userId, now);
            }
        }

        book.Touch(now);
        await SaveAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<BookDetailsDto> UpdateOriginCountriesAsync(Guid id, UpdateOriginCountriesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.GetRequiredUserId();
        var book = await LoadTrackedAsync(id, ct);
        var origins = await CountryResolution.ResolveIsosAsync(db, request.CountryIsos, "countryIsos", ct);
        if (await GeoEditPolicy.CheckOriginsAsync(db, userId, id, origins, ct) == GeoEditDecision.NoOp)
        {
            return await GetAsync(id, ct);
        }

        var now = clock.UtcNow;
        ReplaceOrigins(book, origins, userId, now);
        book.Touch(now);
        await SaveAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<BookDetailsDto> EnrichAsync(Guid id, CancellationToken ct)
    {
        var userId = currentUser.GetRequiredUserId();
        if (!await db.Books.AnyAsync(b => b.Id == id, ct))
        {
            throw NotFound();
        }

        await LibraryAccess.EnsureUserHasBookAsync(db, userId, id, ct);
        await importService.EnrichAsync(id, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Replaces all origins with manual, verified ones (an empty list makes the origin unknown again).</summary>
    private void ReplaceOrigins(Book book, IReadOnlyList<Country> origins, Guid userId, DateTimeOffset now)
    {
        foreach (var relation in book.OriginCountries.ToList())
        {
            book.OriginCountries.Remove(relation);
            db.BookOriginCountries.Remove(relation);
        }

        foreach (var country in origins)
        {
            book.OriginCountries.Add(BookOriginCountry.CreateManual(book.Id, country.Id, userId, now));
        }
    }

    /// <summary>
    /// Contract (§3): existing authors are reused by name. Order of preference: an author already linked to this
    /// book with the same normalized name, then a single catalog author with that normalized name; otherwise a new one.
    /// </summary>
    private async Task ReplaceAuthorsAsync(
        Book book, IReadOnlyList<string> names, IReadOnlyList<Author> existingAuthors, DateTimeOffset now, CancellationToken ct)
    {
        short position = 0;
        var linked = new HashSet<Guid>();
        foreach (var name in names)
        {
            var normalized = Author.NormalizeName(name);
            var author = existingAuthors.FirstOrDefault(a => a.NormalizedName == normalized);
            if (author is null)
            {
                var matches = await db.Authors.Where(a => a.NormalizedName == normalized).Take(2).ToListAsync(ct);
                author = matches.Count == 1 ? matches[0] : null;
            }

            if (author is null)
            {
                author = new Author(name, now);
                db.Authors.Add(author);
            }

            if (linked.Add(author.Id))
            {
                var link = new BookAuthor(book.Id, author.Id, position++);
                book.Authors.Add(link);
            }
        }
    }

    private static IReadOnlyList<string> ValidateRequest(CreateBookRequest request)
    {
        var errors = new ValidationErrors();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > Book.MaxTitleLength)
        {
            errors.Add("title", $"O título deve ter entre 1 e {Book.MaxTitleLength} caracteres.");
        }

        var names = (request.AuthorNames ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0 || names.Count > MaxAuthors)
        {
            errors.Add("authorNames", $"Informe de 1 a {MaxAuthors} autores.");
        }
        else if (names.Any(n => n.Length > Author.MaxNameLength))
        {
            errors.Add("authorNames", $"Cada nome deve ter no máximo {Author.MaxNameLength} caracteres.");
        }

        if (request.PublicationYear is < 1 or > 2999)
        {
            errors.Add("publicationYear", "Ano de publicação inválido.");
        }

        if (request.CoverUrl is { Length: > 1000 })
        {
            errors.Add("coverUrl", "URL de capa inválida.");
        }

        if (request.OriginalLanguage is { Length: > 10 })
        {
            errors.Add("originalLanguage", "Use um código de idioma BCP-47/ISO 639.");
        }

        if (request.Genres is { Count: > MaxGenres })
        {
            errors.Add("genres", $"Informe no máximo {MaxGenres} gêneros.");
        }

        errors.ThrowIfAny();
        return names;
    }

    private static void ApplyBibliographicData(Book book, CreateBookRequest request)
    {
        book.Subtitle = Clean(request.Subtitle, 500);
        book.Publisher = Clean(request.Publisher, 300);
        book.PublicationYear = (short?)request.PublicationYear;
        book.Description = Clean(request.Description, int.MaxValue);
        book.OriginalLanguage = Clean(request.OriginalLanguage, 10);
        book.Genres = (request.Genres ?? [])
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        book.SetIsbns(request.Isbn13, request.Isbn10);
    }

    /// <summary>
    /// Contract (api-contracts v1.6): the book's own internal cover URL keeps the uploaded cover; an external https URL
    /// replaces it (and deletes the uploaded file); null/empty removes the cover. Other relative paths are rejected.
    /// </summary>
    private async Task ApplyCoverAsync(Book book, string? requested, bool isNew, CancellationToken ct)
    {
        var url = string.IsNullOrWhiteSpace(requested) ? null : requested.Trim();
        if (url is null)
        {
            await RemoveUploadedCoverAsync(book.Id, isNew, ct);
            book.CoverUrl = null;
            return;
        }

        var internalPath = BookCover.InternalPath(book.Id);
        if (url == internalPath || url.StartsWith(internalPath + "?", StringComparison.Ordinal))
        {
            if (!isNew && await db.BookCovers.AnyAsync(c => c.BookId == book.Id, ct))
            {
                return; // keep the uploaded cover and the current internal URL
            }

            throw ValidationException.ForField("validation.failed", "coverUrl", "Este livro não tem capa enviada.");
        }

        var unchangedLegacy = !isNew && string.Equals(url, book.CoverUrl, StringComparison.Ordinal)
                              && Uri.TryCreate(url, UriKind.Absolute, out var legacy) && legacy.Scheme == Uri.UriSchemeHttp;
        if (!unchangedLegacy
            && !(Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme == Uri.UriSchemeHttps))
        {
            throw ValidationException.ForField(
                "validation.failed", "coverUrl", "URL de capa inválida (use https:// ou a capa enviada deste livro).");
        }

        await RemoveUploadedCoverAsync(book.Id, isNew, ct);
        book.CoverUrl = url;
    }

    private async Task RemoveUploadedCoverAsync(Guid bookId, bool isNew, CancellationToken ct)
    {
        if (isNew)
        {
            return;
        }

        var cover = await db.BookCovers.SingleOrDefaultAsync(c => c.BookId == bookId, ct);
        if (cover is not null)
        {
            db.BookCovers.Remove(cover);
        }
    }

    private async Task EnsureIsbnAvailableAsync(Book book, CancellationToken ct)
    {
        if (book.Isbn13 is not null && await db.Books.AnyAsync(b => b.Isbn13 == book.Isbn13 && b.Id != book.Id, ct))
        {
            throw new ConflictException("book.duplicate_isbn", "Já existe um livro com este ISBN no catálogo.");
        }
    }

    private async Task<Book> LoadTrackedAsync(Guid id, CancellationToken ct) =>
        await db.Books
            .Include(b => b.Authors).ThenInclude(ba => ba.Author)
            .Include(b => b.OriginCountries)
            .SingleOrDefaultAsync(b => b.Id == id, ct)
        ?? throw NotFound();

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.DiscardChanges();
            throw new ConflictException("book.duplicate_isbn", "Já existe um livro com este ISBN no catálogo.");
        }
    }

    private static string? Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim() is var t && t.Length > max ? t[..max] : value.Trim();

    private static NotFoundException NotFound() => new("book.not_found", "Livro não encontrado no catálogo.");
}
