using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Application.Common;
using AtlasLiterario.Application.ExternalSources;
using AtlasLiterario.Domain.Common;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtlasLiterario.Application.Books;

public interface IBookImportService
{
    /// <summary>
    /// Finds or creates the catalog book for an external source (idempotent), resolves its authors and
    /// enriches geography synchronously. Returns the book id.
    /// </summary>
    /// <summary>Imports (or finds) the book and, when it is not yet Completed, enriches it within the request budget.</summary>
    Task<Guid> ImportAsync(ExternalSourceRef source, CancellationToken ct);

    /// <summary>Creates or finds the book and persists it (Pending). No geographic enrichment.</summary>
    Task<Guid> ImportWithoutEnrichmentAsync(ExternalSourceRef source, CancellationToken ct);

    /// <summary>
    /// Best-effort enrichment inside the remaining request budget; does nothing when the book is Completed.
    /// Never throws for slow/unavailable sources: unfinished work leaves Partial/Failed/Pending.
    /// </summary>
    Task EnrichIfNeededAsync(Guid bookId, CancellationToken ct);

    /// <summary>Re-resolves author identities and geography for an existing book. Manual relations are preserved.</summary>
    Task EnrichAsync(Guid bookId, CancellationToken ct);
}

public sealed partial class BookImportService(
    IAppDbContext db,
    IEnumerable<IBookSearchProvider> providers,
    IAuthorIdentityResolver authorResolver,
    IGeoResolver geoResolver,
    IClock clock,
    ILogger<BookImportService> logger,
    RequestBudget? requestBudget = null) : IBookImportService
{
    /// <summary>Upper bound for the synchronous geographic enrichment of one book.</summary>
    public static readonly TimeSpan EnrichmentBudget = DefaultEnrichmentSettings.DefaultBudget;

    private static readonly TimeSpan MaxComplementTime = TimeSpan.FromSeconds(4);

    private readonly RequestBudget _budget = requestBudget ?? new RequestBudget();

    private const int MaxGenres = 5;

    public async Task<Guid> ImportAsync(ExternalSourceRef source, CancellationToken ct)
    {
        var bookId = await ImportWithoutEnrichmentAsync(source, ct);
        await EnrichIfNeededAsync(bookId, ct);
        return bookId;
    }

    public async Task EnrichIfNeededAsync(Guid bookId, CancellationToken ct)
    {
        var status = await db.Books.AsNoTracking().Where(b => b.Id == bookId).Select(b => (GeoEnrichmentStatus?)b.GeoEnrichmentStatus)
            .SingleOrDefaultAsync(ct);
        if (status is null || status == GeoEnrichmentStatus.Completed)
        {
            return;
        }

        try
        {
            // Failed/Partial books were already tried: also re-resolve author identities. A fresh (Pending) import was just resolved.
            await EnrichGeographyAsync(bookId, reresolveAuthors: status != GeoEnrichmentStatus.Pending, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogEnrichmentSkipped(logger, bookId, ex.GetType().Name);
        }
    }

    public async Task<Guid> ImportWithoutEnrichmentAsync(ExternalSourceRef source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(source.ExternalId))
        {
            throw ValidationException.ForField("book.invalid_source", "source.externalId", "Informe o identificador externo.");
        }

        var externalId = source.ExternalId.Trim();
        if (source.Provider == ExternalProvider.Local)
        {
            return Guid.TryParse(externalId, out var localId) && await db.Books.AnyAsync(b => b.Id == localId, ct)
                ? localId
                : throw new NotFoundException("book.not_found", "Livro não encontrado no catálogo.");
        }

        var identifierProvider = BookDetailsMapping.ToIdentifierProvider(new ExternalSourceRef(source.Provider, externalId));
        var provider = providers.FirstOrDefault(p => p.Provider == source.Provider);
        if (identifierProvider is null || provider is null)
        {
            throw ValidationException.ForField(
                "book.invalid_source", "source", "Fonte não suportada para importação (use GoogleBooks ou OpenLibrary).");
        }

        var existing = await FindBookIdByIdentifierAsync(identifierProvider.Value, externalId, ct);
        if (existing is not null)
        {
            return existing.Value;
        }

        ExternalBookCandidate candidate;
        using (var fetch = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            fetch.CancelAfter(_budget.Remaining);
            try
            {
                candidate = await provider.GetAsync(externalId, fetch.Token)
                            ?? throw new NotFoundException("book.source_not_found", "O livro não foi encontrado na fonte externa.");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new ExternalServiceUnavailableException(source.Provider.ToString(), "Tempo esgotado ao consultar a fonte externa.");
            }
        }

        candidate = await ComplementWithOpenLibraryAsync(candidate, ct);

        var bookId = await FindExistingBookAsync(candidate, ct);
        if (bookId is not null)
        {
            await AttachIdentifiersAsync(bookId.Value, candidate, ct);
            return bookId.Value;
        }

        var book = await CreateBookAsync(candidate, ct);
        try
        {
            // Commit independent of RequestAborted: the imported book must not be lost half-way.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException)
        {
            // Concurrent import of the same book (unique ISBN-13 / external identifier).
            db.DiscardChanges();
            return await FindExistingBookAsync(candidate, ct) ?? throw new ConflictException(
                "book.import_conflict", "O livro está sendo importado por outra requisição. Tente novamente.");
        }

        return book.Id;
    }

    public async Task EnrichAsync(Guid bookId, CancellationToken ct)
    {
        if (!await db.Books.AnyAsync(b => b.Id == bookId, ct))
        {
            throw new NotFoundException("book.not_found", "Livro não encontrado no catálogo.");
        }

        await EnrichGeographyAsync(bookId, reresolveAuthors: true, ct);
    }

    /// <summary>Google Books gives weak author identity: use Open Library (by ISBN) for work id, author keys and first year.</summary>
    private async Task<ExternalBookCandidate> ComplementWithOpenLibraryAsync(ExternalBookCandidate candidate, CancellationToken ct)
    {
        if (candidate.Provider == ExternalProvider.OpenLibrary || candidate.Isbn13 is null)
        {
            return candidate;
        }

        var openLibrary = providers.FirstOrDefault(p => p.Provider == ExternalProvider.OpenLibrary);
        if (openLibrary is null)
        {
            return candidate;
        }

        // The complement is optional: it gets at most a third of what is left (and never more than 4 s).
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromTicks(Math.Min(MaxComplementTime.Ticks, _budget.Remaining.Ticks / 3)));
        try
        {
            var match = (await openLibrary.SearchAsync(new BookSearchQuery(candidate.Isbn13, BookSearchType.Isbn, 1), limit.Token))
                .FirstOrDefault(c => c.OpenLibraryWorkId is not null);
            if (match is null)
            {
                return candidate;
            }

            return candidate with
            {
                OpenLibraryWorkId = match.OpenLibraryWorkId,
                // Open Library identity (keys). For a single author the source's name is kept, so the resolver can
                // prefer a Latin-script display name; with several authors the order may differ, so OL names are used.
                Authors = match.Authors.Count == 0 ? candidate.Authors
                    : match.Authors.Count == 1 && candidate.Authors.Count == 1
                        ? [match.Authors[0] with { Name = candidate.Authors[0].Name }]
                        : match.Authors,
                PublicationYear = match.PublicationYear ?? candidate.PublicationYear,
                CoverUrl = candidate.CoverUrl ?? match.CoverUrl,
                Description = candidate.Description ?? match.Description,
            };
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LogComplementFailed(logger, candidate.Isbn13, ex.Message);
            return candidate;
        }
    }

    private async Task<Guid?> FindBookIdByIdentifierAsync(BookIdentifierProvider provider, string externalId, CancellationToken ct) =>
        await db.ExternalBookIdentifiers
            .Where(i => i.Provider == provider && i.ExternalId == externalId)
            .Select(i => (Guid?)i.BookId)
            .FirstOrDefaultAsync(ct);

    /// <summary>Deduplication: ISBN-13 → external identifiers (never by title alone).</summary>
    private async Task<Guid?> FindExistingBookAsync(ExternalBookCandidate candidate, CancellationToken ct)
    {
        var isbn13 = Isbn.ToIsbn13(candidate.Isbn13) ?? Isbn.ToIsbn13(candidate.Isbn10);
        if (isbn13 is not null)
        {
            var byIsbn = await db.Books.Where(b => b.Isbn13 == isbn13).Select(b => (Guid?)b.Id).FirstOrDefaultAsync(ct);
            if (byIsbn is not null)
            {
                return byIsbn;
            }
        }

        foreach (var (provider, id) in CandidateIdentifiers(candidate))
        {
            var found = await FindBookIdByIdentifierAsync(provider, id, ct);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private async Task AttachIdentifiersAsync(Guid bookId, ExternalBookCandidate candidate, CancellationToken ct)
    {
        var added = false;
        foreach (var (provider, id) in CandidateIdentifiers(candidate))
        {
            if (await db.ExternalBookIdentifiers.AnyAsync(i => i.Provider == provider && i.ExternalId == id, ct))
            {
                continue;
            }

            db.ExternalBookIdentifiers.Add(new ExternalBookIdentifier(bookId, provider, id));
            added = true;
        }

        if (!added)
        {
            return;
        }

        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException)
        {
            db.DiscardChanges(); // another request attached them first
        }
    }

    private static IEnumerable<(BookIdentifierProvider Provider, string Id)> CandidateIdentifiers(ExternalBookCandidate candidate)
    {
        var own = BookDetailsMapping.ToIdentifierProvider(new ExternalSourceRef(candidate.Provider, candidate.ExternalId));
        if (own is not null)
        {
            yield return (own.Value, candidate.ExternalId);
        }

        if (candidate.OpenLibraryWorkId is not null
            && !(own == BookIdentifierProvider.OpenLibraryWork && candidate.ExternalId == candidate.OpenLibraryWorkId))
        {
            yield return (BookIdentifierProvider.OpenLibraryWork, candidate.OpenLibraryWorkId);
        }
    }

    private async Task<Book> CreateBookAsync(ExternalBookCandidate candidate, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var book = new Book(Truncate(candidate.Title, Book.MaxTitleLength)!, now)
        {
            Subtitle = Truncate(candidate.Subtitle, 500),
            Publisher = Truncate(candidate.Publisher, 300),
            PublicationYear = candidate.PublicationYear is > 0 and <= short.MaxValue ? (short)candidate.PublicationYear : null,
            Description = string.IsNullOrWhiteSpace(candidate.Description) ? null : candidate.Description.Trim(),
            CoverUrl = candidate.CoverUrl is { Length: <= 1000 } ? candidate.CoverUrl : null,
            Genres = candidate.Categories
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxGenres)
                .ToList(),
            GeoEnrichmentStatus = GeoEnrichmentStatus.Pending,
        };

        // Invalid ISBNs from sources are dropped, never stored.
        var isbn13 = Isbn.IsValidIsbn13(Isbn.Normalize(candidate.Isbn13)) ? candidate.Isbn13 : null;
        var isbn10 = Isbn.IsValidIsbn10(Isbn.Normalize(candidate.Isbn10)) ? candidate.Isbn10 : null;
        book.SetIsbns(isbn13, isbn10);
        db.Books.Add(book);

        foreach (var (provider, id) in CandidateIdentifiers(candidate))
        {
            db.ExternalBookIdentifiers.Add(new ExternalBookIdentifier(book.Id, provider, id));
        }

        var authors = await ResolveAuthorsAsync(candidate, ct);
        var position = (short)0;
        var linked = new HashSet<Guid>();
        foreach (var resolved in authors)
        {
            var author = await FindOrCreateAuthorAsync(resolved, now, ct);
            if (linked.Add(author.Id))
            {
                db.BookAuthors.Add(new BookAuthor(book.Id, author.Id, position++));
            }
        }

        return book;
    }

    private async Task<IReadOnlyList<ResolvedAuthor>> ResolveAuthorsAsync(ExternalBookCandidate candidate, CancellationToken ct)
    {
        // Optional identity lookup: at most 6 s and never beyond the request budget (falls back to names only).
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromTicks(Math.Min(TimeSpan.FromSeconds(6).Ticks, _budget.Remaining.Ticks)));
        try
        {
            var resolved = await authorResolver.ResolveAuthorsAsync(candidate, limit.Token);
            if (resolved.Count > 0)
            {
                return resolved;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LogAuthorResolutionFailed(logger, candidate.ExternalId, ex.Message);
        }

        // Fallback: names only (identity unknown).
        return candidate.Authors
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Select(a => new ResolvedAuthor(a.Name.Trim(), a.OpenLibraryKey, null, null, null))
            .ToList();
    }

    /// <summary>Author deduplication: Wikidata QID → Open Library key. Without identifiers a new author is created
    /// (database.md: name matching only with co-occurrence in the same work, impossible for a new work).</summary>
    private async Task<Author> FindOrCreateAuthorAsync(ResolvedAuthor resolved, DateTimeOffset now, CancellationToken ct)
    {
        Author? author = null;
        if (resolved.WikidataQid is not null)
        {
            author = await FindAuthorByIdentifierAsync(AuthorIdentifierProvider.Wikidata, resolved.WikidataQid, ct);
        }

        if (author is null && resolved.OpenLibraryKey is not null)
        {
            author = await FindAuthorByIdentifierAsync(AuthorIdentifierProvider.OpenLibrary, resolved.OpenLibraryKey, ct);
        }

        if (author is null)
        {
            author = new Author(Truncate(resolved.Name, Author.MaxNameLength)!, now) { BirthDate = resolved.BirthDate };
            db.Authors.Add(author);
        }
        else
        {
            author.BirthDate ??= resolved.BirthDate;
        }

        await AddAuthorIdentifierAsync(author, AuthorIdentifierProvider.Wikidata, resolved.WikidataQid, ct);
        await AddAuthorIdentifierAsync(author, AuthorIdentifierProvider.OpenLibrary, resolved.OpenLibraryKey, ct);
        await AddAuthorIdentifierAsync(author, AuthorIdentifierProvider.Viaf, resolved.ViafId, ct);
        return author;
    }

    private async Task<Author?> FindAuthorByIdentifierAsync(AuthorIdentifierProvider provider, string externalId, CancellationToken ct)
    {
        var tracked = db.ExternalAuthorIdentifiers.Local
            .FirstOrDefault(i => i.Provider == provider && i.ExternalId == externalId);
        var authorId = tracked?.AuthorId ?? await db.ExternalAuthorIdentifiers
            .Where(i => i.Provider == provider && i.ExternalId == externalId)
            .Select(i => (Guid?)i.AuthorId)
            .FirstOrDefaultAsync(ct);
        return authorId is null ? null : await db.Authors.FindAsync([authorId.Value], ct);
    }

    private async Task AddAuthorIdentifierAsync(
        Author author, AuthorIdentifierProvider provider, string? externalId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(externalId) || externalId.Length > ExternalAuthorIdentifier.MaxExternalIdLength)
        {
            return;
        }

        var inMemory = db.ExternalAuthorIdentifiers.Local.Any(i => i.Provider == provider && i.ExternalId == externalId);
        if (inMemory || await db.ExternalAuthorIdentifiers.AnyAsync(i => i.Provider == provider && i.ExternalId == externalId, ct))
        {
            return;
        }

        // One identifier per provider and author.
        if (db.ExternalAuthorIdentifiers.Local.Any(i => i.AuthorId == author.Id && i.Provider == provider)
            || await db.ExternalAuthorIdentifiers.AnyAsync(i => i.AuthorId == author.Id && i.Provider == provider, ct))
        {
            return;
        }

        db.ExternalAuthorIdentifiers.Add(new ExternalAuthorIdentifier(author.Id, provider, externalId));
    }

    private async Task EnrichGeographyAsync(Guid bookId, bool reresolveAuthors, CancellationToken ct)
    {
        var remaining = _budget.Remaining;
        if (remaining <= TimeSpan.Zero)
        {
            LogEnrichmentTimedOut(logger, bookId);
            return; // nothing left of the request budget: the book stays as it is (Pending/Partial/Failed)
        }

        // Cancelled by the time budget OR by the client aborting; the final commit below never uses `ct`.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(remaining);

        var book = await db.Books
            .Include(b => b.Authors).ThenInclude(ba => ba.Author!).ThenInclude(a => a.Countries)
            .Include(b => b.Authors).ThenInclude(ba => ba.Author!).ThenInclude(a => a.ExternalIdentifiers)
            .Include(b => b.OriginCountries)
            .Include(b => b.ExternalIdentifiers)
            .SingleAsync(b => b.Id == bookId, ct);
        var countryIds = await db.Countries.AsNoTracking().ToDictionaryAsync(c => c.IsoCode, c => c.Id, StringComparer.Ordinal, ct);

        var outcome = new EnrichmentOutcome();
        var now = clock.UtcNow;
        var authors = book.Authors.OrderBy(ba => ba.Position).Select(ba => ba.Author!).ToList();

        try
        {
            if (reresolveAuthors)
            {
                await ReresolveAuthorIdentitiesAsync(authors, outcome, budget.Token);
            }

            foreach (var author in authors)
            {
                await EnrichAuthorAsync(author, countryIds, outcome, now, budget.Token);
            }

            await EnrichWorkAsync(book, authors, countryIds, outcome, now, budget.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ExternalServiceUnavailableException)
        {
            outcome.Errors++;
            LogEnrichmentTimedOut(logger, bookId);
        }

        book.GeoEnrichmentStatus = ComputeStatus(book, authors, outcome);
        book.Touch(now);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Authors without a Wikidata QID get another identity lookup (Open Library key, or name for name-only authors).</summary>
    private async Task ReresolveAuthorIdentitiesAsync(List<Author> authors, EnrichmentOutcome outcome, CancellationToken ct)
    {
        var missing = authors
            .Where(a => a.ExternalIdentifiers.All(i => i.Provider != AuthorIdentifierProvider.Wikidata))
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var probe = new ExternalBookCandidate(
            ExternalProvider.OpenLibrary, "reresolve", "reresolve", null,
            missing.Select(a => new ExternalAuthorRef(
                a.Name, a.ExternalIdentifiers.FirstOrDefault(i => i.Provider == AuthorIdentifierProvider.OpenLibrary)?.ExternalId)).ToList(),
            null, null, null, null, null, null, [], null, null);
        try
        {
            var resolved = await authorResolver.ResolveAuthorsAsync(probe, ct);
            for (var i = 0; i < missing.Count && i < resolved.Count; i++)
            {
                var author = missing[i];
                author.BirthDate ??= resolved[i].BirthDate;
                await AddAuthorIdentifierAsync(author, AuthorIdentifierProvider.OpenLibrary, resolved[i].OpenLibraryKey, ct);
                await AddAuthorIdentifierAsync(author, AuthorIdentifierProvider.Wikidata, resolved[i].WikidataQid, ct);
                await AddAuthorIdentifierAsync(author, AuthorIdentifierProvider.Viaf, resolved[i].ViafId, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            outcome.Errors++;
            LogAuthorResolutionFailed(logger, "reresolve", ex.Message);
        }
    }

    private async Task EnrichAuthorAsync(
        Author author, Dictionary<string, short> countryIds, EnrichmentOutcome outcome, DateTimeOffset now, CancellationToken ct)
    {
        var qid = AuthorQid(author);
        if (qid is null)
        {
            return; // identity unknown → nationality stays unknown
        }

        // Manual citizenships always win: automatic reprocessing never touches them.
        if (author.Countries.Any(c => c.RelationshipType == AuthorCountryRelationship.Citizenship && c.IsManual))
        {
            return;
        }

        IReadOnlyList<ResolvedCountry> resolved;
        try
        {
            resolved = await geoResolver.GetAuthorCitizenshipsAsync(qid, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            outcome.Errors++;
            LogGeoFailed(logger, qid, ex.Message);
            return;
        }

        outcome.Successes++;
        var toRemove = author.Countries
            .Where(c => c.RelationshipType == AuthorCountryRelationship.Citizenship && !c.IsManual)
            .ToList();
        foreach (var relation in toRemove)
        {
            author.Countries.Remove(relation);
            db.AuthorCountries.Remove(relation);
        }

        foreach (var country in DistinctKnown(resolved, countryIds))
        {
            author.Countries.Add(new AuthorCountry(
                author.Id, countryIds[country.IsoCode], AuthorCountryRelationship.Citizenship, country.Source,
                country.Confidence, isVerified: false, updatedByUserId: null, now));
        }

        author.Touch(now);
    }

    private async Task EnrichWorkAsync(
        Book book, List<Author> authors, Dictionary<string, short> countryIds, EnrichmentOutcome outcome,
        DateTimeOffset now, CancellationToken ct)
    {
        string? workQid = book.ExternalIdentifiers.FirstOrDefault(i => i.Provider == BookIdentifierProvider.Wikidata)?.ExternalId;
        try
        {
            if (workQid is null)
            {
                var olWork = book.ExternalIdentifiers.FirstOrDefault(i => i.Provider == BookIdentifierProvider.OpenLibraryWork)?.ExternalId;
                var firstAuthorQid = authors.Select(AuthorQid).FirstOrDefault(q => q is not null);
                workQid = await geoResolver.FindWorkQidAsync(book.Isbn13, olWork, book.Title, firstAuthorQid, ct);
                if (workQid is not null
                    && !await db.ExternalBookIdentifiers.AnyAsync(
                        i => i.Provider == BookIdentifierProvider.Wikidata && i.ExternalId == workQid, ct))
                {
                    book.ExternalIdentifiers.Add(new ExternalBookIdentifier(book.Id, BookIdentifierProvider.Wikidata, workQid));
                }
            }

            outcome.Successes++;
            if (workQid is null)
            {
                return; // work not identified → origin stays unknown
            }

            if (!book.OriginCountries.Any(o => o.IsManual))
            {
                var origins = await geoResolver.GetWorkOriginsAsync(workQid, ct);
                foreach (var relation in book.OriginCountries.ToList())
                {
                    book.OriginCountries.Remove(relation);
                    db.BookOriginCountries.Remove(relation);
                }

                foreach (var country in DistinctKnown(origins, countryIds))
                {
                    book.OriginCountries.Add(new BookOriginCountry(
                        book.Id, countryIds[country.IsoCode], country.Source, country.Confidence, false, null, now));
                }
            }

            book.OriginalLanguage ??= await geoResolver.GetWorkOriginalLanguageAsync(workQid, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            outcome.Errors++;
            LogGeoFailed(logger, workQid ?? book.Title, ex.Message);
        }
    }

    private static IEnumerable<ResolvedCountry> DistinctKnown(
        IEnumerable<ResolvedCountry> countries, Dictionary<string, short> countryIds) =>
        countries
            .Where(c => countryIds.ContainsKey(c.IsoCode))
            .GroupBy(c => c.IsoCode, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(c => c.Confidence).First());

    private static string? AuthorQid(Author author) =>
        author.ExternalIdentifiers.FirstOrDefault(i => i.Provider == AuthorIdentifierProvider.Wikidata)?.ExternalId;

    /// <summary>
    /// Completed: every author has a citizenship and the work has an origin. Failed: sources failed and nothing was obtained.
    /// Partial: anything in between (missing data or partial failures). Missing data is never treated as "no country".
    /// </summary>
    internal static GeoEnrichmentStatus ComputeStatus(Book book, IReadOnlyCollection<Author> authors, EnrichmentOutcome outcome)
    {
        var allAuthorsKnown = authors.Count > 0 && authors.All(a =>
            a.Countries.Any(c => c.RelationshipType == AuthorCountryRelationship.Citizenship));
        var originKnown = book.OriginCountries.Count > 0;

        if (outcome.Errors > 0 && outcome.Successes == 0)
        {
            return GeoEnrichmentStatus.Failed;
        }

        return outcome.Errors == 0 && allAuthorsKnown && originKnown
            ? GeoEnrichmentStatus.Completed
            : GeoEnrichmentStatus.Partial;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    internal sealed class EnrichmentOutcome
    {
        public int Errors { get; set; }

        public int Successes { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Open Library complement for ISBN {Isbn} failed: {Reason}")]
    private static partial void LogComplementFailed(ILogger logger, string? isbn, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Author identity resolution for {Source} failed: {Reason}")]
    private static partial void LogAuthorResolutionFailed(ILogger logger, string source, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Wikidata lookup for {Entity} failed: {Reason}")]
    private static partial void LogGeoFailed(ILogger logger, string entity, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geographic enrichment of book {BookId} skipped: {Reason}")]
    private static partial void LogEnrichmentSkipped(ILogger logger, Guid bookId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geographic enrichment of book {BookId} exceeded its time budget")]
    private static partial void LogEnrichmentTimedOut(ILogger logger, Guid bookId);
}
