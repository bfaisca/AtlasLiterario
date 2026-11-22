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

public interface IBookSearchService
{
    Task<IReadOnlyList<BookSearchResultDto>> SearchAsync(string? q, string? type, int? limit, CancellationToken ct);
}

/// <summary>
/// Consolidated search: local catalog first, then every external provider in parallel.
/// A failing provider never breaks the search; 502 only when all providers fail and nothing local was found.
/// Results are deduplicated by ISBN-13 and by normalized title + first author. Nothing is persisted.
/// </summary>
public sealed partial class BookSearchService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IEnumerable<IBookSearchProvider> providers,
    ILogger<BookSearchService> logger) : IBookSearchService
{
    public const int MinTermLength = 2;
    public const int DefaultLimit = 20;
    public const int MaxLimit = 40;
    public const int MaxDescriptionLength = 500;

    public async Task<IReadOnlyList<BookSearchResultDto>> SearchAsync(
        string? q, string? type, int? limit, CancellationToken ct)
    {
        var query = ParseQuery(q, type, limit);
        var userId = currentUser.GetRequiredUserId();

        var localBooks = await SearchLocalAsync(query, ct);
        var external = await SearchExternalAsync(query, ct);
        if (external.AllFailed && localBooks.Count == 0)
        {
            throw new ExternalServiceUnavailableException(
                "search", "Nenhuma fonte bibliográfica respondeu. Tente novamente em instantes.");
        }

        // External candidates already imported (same external id) are shown as the local book.
        var externalIds = external.Candidates.Select(c => c.ExternalId).Distinct().ToList();
        var identifiers = await db.ExternalBookIdentifiers.AsNoTracking()
            .Where(i => externalIds.Contains(i.ExternalId))
            .Select(i => new { i.BookId, i.Provider, i.ExternalId })
            .ToListAsync(ct);
        var importedBookIds = identifiers.Select(i => i.BookId).Except(localBooks.Select(b => b.Id)).ToList();
        if (importedBookIds.Count > 0)
        {
            localBooks.AddRange(await db.Books.AsNoTracking()
                .Include(b => b.Authors).ThenInclude(ba => ba.Author)
                .Where(b => importedBookIds.Contains(b.Id))
                .ToListAsync(ct));
        }

        var localIds = localBooks.Select(b => b.Id).ToList();
        var inLibrary = (await db.UserBooks.AsNoTracking()
            .Where(ub => ub.UserId == userId && localIds.Contains(ub.BookId))
            .Select(ub => new { ub.BookId, ub.Id })
            .ToListAsync(ct)).ToDictionary(x => x.BookId, x => x.Id);

        var results = new List<BookSearchResultDto>();
        var seen = new DedupKeys();
        foreach (var book in localBooks)
        {
            var dto = ToResult(book, inLibrary.TryGetValue(book.Id, out var itemId) ? itemId : null);
            seen.Add(dto);
            results.Add(dto);
        }

        var identifierLookup = identifiers.Select(i => (i.Provider, i.ExternalId)).ToHashSet();
        foreach (var candidate in external.Candidates)
        {
            if (IsAlreadyImported(candidate, identifierLookup))
            {
                continue;
            }

            var dto = ToResult(candidate);
            if (seen.Contains(dto))
            {
                continue;
            }

            seen.Add(dto);
            results.Add(dto);
        }

        return results.Take(query.Limit).ToList();
    }

    internal static BookSearchQuery ParseQuery(string? q, string? type, int? limit)
    {
        var errors = new ValidationErrors();
        var term = (q ?? string.Empty).Trim();
        if (term.Length < MinTermLength)
        {
            errors.Add("q", $"Informe ao menos {MinTermLength} caracteres.");
        }

        BookSearchType searchType = BookSearchType.All;
        if (!string.IsNullOrWhiteSpace(type)
            && (!Enum.TryParse(type, ignoreCase: true, out searchType) || !Enum.IsDefined(searchType)))
        {
            errors.Add("type", "Tipo de pesquisa inválido (all, title, author, isbn).");
        }

        errors.ThrowIfAny();

        var isbn13 = Isbn.ToIsbn13(term);
        if (searchType == BookSearchType.Isbn && isbn13 is null)
        {
            throw ValidationException.ForField("book.invalid_isbn13", "q", "ISBN inválido (dígito verificador).");
        }

        // An ISBN typed in the general search is searched as an ISBN.
        if (searchType == BookSearchType.All && isbn13 is not null)
        {
            searchType = BookSearchType.Isbn;
        }

        return new BookSearchQuery(
            searchType == BookSearchType.Isbn ? isbn13! : term,
            searchType,
            Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit));
    }

    private async Task<List<Book>> SearchLocalAsync(BookSearchQuery query, CancellationToken ct)
    {
        var books = db.Books.AsNoTracking().Include(b => b.Authors).ThenInclude(ba => ba.Author).AsQueryable();

        switch (query.Type)
        {
            case BookSearchType.Isbn:
                // ISBN-10 input was converted to ISBN-13, and every stored ISBN-10 also has its ISBN-13.
                books = books.Where(b => b.Isbn13 == query.Term);
                break;
            case BookSearchType.Title:
                var title = query.Term.ToLowerInvariant();
                books = books.Where(b => b.Title.ToLower().Contains(title));
                break;
            case BookSearchType.Author:
                var author = TextNormalizer.NormalizeForComparison(query.Term);
                books = books.Where(b => b.Authors.Any(ba => ba.Author!.NormalizedName.Contains(author)));
                break;
            default:
                // Every word must appear in the title or in an author name.
                foreach (var token in query.Term.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var lower = token.ToLowerInvariant();
                    var normalized = TextNormalizer.NormalizeForComparison(token);
                    books = books.Where(b => b.Title.ToLower().Contains(lower)
                                             || b.Authors.Any(ba => ba.Author!.NormalizedName.Contains(normalized)));
                }

                break;
        }

        return await books.OrderBy(b => b.Title).Take(query.Limit).ToListAsync(ct);
    }

    private async Task<(IReadOnlyList<ExternalBookCandidate> Candidates, bool AllFailed)> SearchExternalAsync(
        BookSearchQuery query, CancellationToken ct)
    {
        // Provider order = metadata priority (Google Books > Open Library) for duplicates.
        var ordered = providers.OrderBy(p => p.Provider).ToList();
        if (ordered.Count == 0)
        {
            return ([], true);
        }

        var tasks = ordered.Select(p => SearchProviderAsync(p, query, ct)).ToList();
        var outcomes = await Task.WhenAll(tasks);
        return (outcomes.Where(o => o is not null).SelectMany(o => o!).ToList(), outcomes.All(o => o is null));
    }

    private async Task<IReadOnlyList<ExternalBookCandidate>?> SearchProviderAsync(
        IBookSearchProvider provider, BookSearchQuery query, CancellationToken ct)
    {
        try
        {
            return await provider.SearchAsync(query, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogProviderFailed(logger, provider.Provider, ex.Message);
            return null;
        }
    }

    private static bool IsAlreadyImported(
        ExternalBookCandidate candidate, HashSet<(BookIdentifierProvider Provider, string ExternalId)> identifiers)
    {
        var provider = BookDetailsMapping.ToIdentifierProvider(new ExternalSourceRef(candidate.Provider, candidate.ExternalId));
        return provider is not null && identifiers.Contains((provider.Value, candidate.ExternalId));
    }

    private static BookSearchResultDto ToResult(Book book, Guid? libraryItemId) =>
        new(
            $"local:{book.Id}",
            book.Id,
            libraryItemId is not null,
            libraryItemId,
            book.Title,
            book.Subtitle,
            book.Authors.OrderBy(a => a.Position).Where(a => a.Author is not null).Select(a => a.Author!.Name).ToList(),
            book.Publisher,
            book.PublicationYear,
            book.Isbn13,
            book.Isbn10,
            book.CoverUrl,
            Truncate(book.Description),
            new ExternalSourceRef(ExternalProvider.Local, book.Id.ToString()));

    private static BookSearchResultDto ToResult(ExternalBookCandidate candidate) =>
        new(
            $"{candidate.Provider}:{candidate.ExternalId}",
            null,
            false,
            null,
            candidate.Title,
            candidate.Subtitle,
            candidate.Authors.Select(a => a.Name).ToList(),
            candidate.Publisher,
            candidate.PublicationYear,
            candidate.Isbn13,
            candidate.Isbn10,
            candidate.CoverUrl,
            Truncate(candidate.Description),
            new ExternalSourceRef(candidate.Provider, candidate.ExternalId));

    internal static string? Truncate(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var trimmed = description.Trim();
        return trimmed.Length <= MaxDescriptionLength ? trimmed : trimmed[..(MaxDescriptionLength - 1)].TrimEnd() + "…";
    }

    /// <summary>Deduplication keys: ISBN-13 and normalized "title|first author".</summary>
    private sealed class DedupKeys
    {
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

        public bool Contains(BookSearchResultDto dto) => Keys(dto).Any(_keys.Contains);

        public void Add(BookSearchResultDto dto) => _keys.UnionWith(Keys(dto));

        private static IEnumerable<string> Keys(BookSearchResultDto dto)
        {
            if (dto.Isbn13 is not null)
            {
                yield return "isbn:" + dto.Isbn13;
            }

            var firstAuthor = dto.Authors.FirstOrDefault();
            if (firstAuthor is not null)
            {
                yield return "ta:" + TextNormalizer.NormalizeForComparison(dto.Title) + "|"
                             + TextNormalizer.NormalizeForComparison(firstAuthor);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Book search provider {Provider} failed: {Reason}")]
    private static partial void LogProviderFailed(ILogger logger, ExternalProvider provider, string reason);
}
