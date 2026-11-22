using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AtlasLiterario.Application.ExternalSources;
using AtlasLiterario.Domain.Common;
using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Infrastructure.ExternalSources;

/// <summary>Open Library: works, editions (ISBN) and author identities (remote_ids → Wikidata QID, VIAF).</summary>
public sealed partial class OpenLibraryClient(HttpClient httpClient, IExternalLookupCache cache, ExternalCooldown? cooldown = null)
    : IBookSearchProvider, IAuthorIdentityResolver
{
    public const string BaseAddress = "https://openlibrary.org/";
    public const string CoverBaseAddress = "https://covers.openlibrary.org/b/id/";
    private const string SourceName = "Open Library";
    private const string SearchFields = "key,title,subtitle,author_name,author_key,first_publish_year,cover_i";

    private static readonly string[] BirthDateFormats = ["d MMMM yyyy", "MMMM d, yyyy", "yyyy-MM-dd", "d MMM yyyy", "MMM d, yyyy"];

    public ExternalProvider Provider => ExternalProvider.OpenLibrary;

    public async Task<IReadOnlyList<ExternalBookCandidate>> SearchAsync(BookSearchQuery query, CancellationToken ct)
    {
        if (query.Type == BookSearchType.Isbn)
        {
            var edition = await GetEditionByIsbnAsync(query.Term, ct);
            return edition is null ? [] : [edition];
        }

        var parameter = query.Type switch
        {
            BookSearchType.Title => "title",
            BookSearchType.Author => "author",
            _ => "q",
        };
        var limit = Math.Clamp(query.Limit, 1, 40);
        var uri = $"search.json?{parameter}={Uri.EscapeDataString(query.Term)}&fields={SearchFields}&limit={limit}";

        return await cache.GetOrCreateAsync<IReadOnlyList<ExternalBookCandidate>>(
            $"openlibrary:search:{query.Type}:{query.Term}:{limit}",
            ExternalLookupCacheStore.SearchTtl,
            async token =>
            {
                using var document = await ExternalHttp.GetJsonAsync(httpClient, uri, SourceName, token, cooldown: cooldown);
                return document is null ? [] : ParseSearch(document.RootElement);
            },
            ct) ?? [];
    }

    /// <summary>Accepts a work id (OL…W) or an edition id (OL…M).</summary>
    public Task<ExternalBookCandidate?> GetAsync(string externalId, CancellationToken ct)
    {
        if (!OpenLibraryId().IsMatch(externalId))
        {
            return Task.FromResult<ExternalBookCandidate?>(null);
        }

        return externalId.EndsWith('W')
            ? GetWorkAsync(externalId, ct)
            : GetEditionAsync($"books/{externalId}.json", $"openlibrary:edition:{externalId}", ct);
    }

    /// <summary>Minimum works of the dominant author for a name-only identification.</summary>
    public const int MinWorksForNameMatch = 20;

    /// <summary>The dominant exact-name match must have at least this many times the works of the next one.</summary>
    public const int NameMatchDominance = 5;

    public async Task<IReadOnlyList<ResolvedAuthor>> ResolveAuthorsAsync(ExternalBookCandidate book, CancellationToken ct)
    {
        var resolved = new List<ResolvedAuthor>();
        foreach (var author in book.Authors)
        {
            var key = author.OpenLibraryKey is not null && OpenLibraryId().IsMatch(author.OpenLibraryKey)
                ? author.OpenLibraryKey
                : await FindAuthorKeyByNameAsync(author.Name, ct);
            if (key is null)
            {
                resolved.Add(new ResolvedAuthor(author.Name, null, null, null, null));
                continue;
            }

            var details = await GetAuthorAsync(key, ct);
            resolved.Add(details is null
                ? new ResolvedAuthor(author.Name, key, null, null, null)
                : details with { Name = PreferDisplayName(author.Name, details.Name) });
        }

        return resolved;
    }

    /// <summary>
    /// Keeps the source's name when Open Library's name has no Latin letters (e.g. "村上春樹" vs "Haruki Murakami").
    /// </summary>
    internal static string PreferDisplayName(string sourceName, string openLibraryName) =>
        !HasLatinLetter(openLibraryName) && HasLatinLetter(sourceName) && sourceName != KeyLikeName(sourceName)
            ? sourceName
            : openLibraryName;

    private static bool HasLatinLetter(string value) =>
        value.Any(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '\u00C0' and <= '\u024F'));

    /// <summary>Author refs built from bare keys carry the key as name; never prefer those.</summary>
    private static string KeyLikeName(string name) => OpenLibraryId().IsMatch(name) ? name : string.Empty;

    /// <summary>
    /// Name-only identity (e.g. authors from Google Books): exact normalized match on name or alternate names, accepted only
    /// when one match clearly dominates by number of works. Ambiguous or weak matches stay unknown.
    /// </summary>
    private async Task<string?> FindAuthorKeyByNameAsync(string name, CancellationToken ct)
    {
        var normalized = TextNormalizer.NormalizeForComparison(name);
        if (normalized.Length < 3 || !normalized.Contains(' '))
        {
            return null; // single words (surnames) are too ambiguous
        }

        var match = await cache.GetOrCreateAsync<AuthorMatch>(
            $"openlibrary:authorsearch:{normalized}",
            ExternalLookupCacheStore.EntityTtl,
            async token =>
            {
                var uri = $"search/authors.json?q={Uri.EscapeDataString(name)}&limit=10";
                using var document = await ExternalHttp.GetJsonAsync(httpClient, uri, SourceName, token, cooldown: cooldown);
                return new AuthorMatch(document is null ? null : SelectDominantAuthor(document.RootElement, normalized));
            },
            ct);
        return match?.Key;
    }

    internal static string? SelectDominantAuthor(JsonElement root, string normalizedName)
    {
        var matches = root.Arr("docs")
            .Where(d => d.Strings("alternate_names").Prepend(d.Str("name") ?? string.Empty)
                .Any(n => TextNormalizer.NormalizeForComparison(n) == normalizedName))
            .Select(d => (Key: KeyId(d.Str("key")), Works: d.Int("work_count") ?? 0))
            .Where(m => m.Key is not null)
            .OrderByDescending(m => m.Works)
            .ToList();
        if (matches.Count == 0 || matches[0].Works < MinWorksForNameMatch)
        {
            return null;
        }

        var runnerUp = matches.Count > 1 ? matches[1].Works : 0;
        return matches[0].Works >= NameMatchDominance * Math.Max(runnerUp, 1) ? matches[0].Key : null;
    }

    public sealed record AuthorMatch(string? Key);

    private Task<ExternalBookCandidate?> GetEditionByIsbnAsync(string isbn13, CancellationToken ct) =>
        GetEditionAsync($"isbn/{isbn13}.json", $"openlibrary:isbn:{isbn13}", ct);

    private Task<ExternalBookCandidate?> GetEditionAsync(string uri, string cacheKey, CancellationToken ct) =>
        cache.GetOrCreateAsync(cacheKey, ExternalLookupCacheStore.EntityTtl, async token =>
        {
            using var document = await ExternalHttp.GetJsonAsync(httpClient, uri, SourceName, token, cooldown: cooldown);
            if (document is null)
            {
                return null;
            }

            var edition = document.RootElement;
            var editionId = KeyId(edition.Str("key"));
            var workId = edition.Arr("works").Select(w => KeyId(w.Str("key"))).FirstOrDefault(id => id is not null);
            var work = workId is null ? null : await GetWorkAsync(workId, token);
            return editionId is null ? null : ParseEdition(edition, editionId, work);
        }, ct);

    private Task<ExternalBookCandidate?> GetWorkAsync(string workId, CancellationToken ct) =>
        cache.GetOrCreateAsync($"openlibrary:work:{workId}", ExternalLookupCacheStore.EntityTtl, async token =>
        {
            using var work = await ExternalHttp.GetJsonAsync(httpClient, $"works/{workId}.json", SourceName, token, cooldown: cooldown);
            if (work is null)
            {
                return null;
            }

            // The search index carries author names and first_publish_year, which the work record lacks.
            var searchUri = $"search.json?q={Uri.EscapeDataString("key:/works/" + workId)}&fields={SearchFields}&limit=1";
            using var search = await ExternalHttp.GetJsonAsync(httpClient, searchUri, SourceName, token, cooldown: cooldown);
            JsonElement? doc = search?.RootElement.Arr("docs").FirstOrDefault();
            return ParseWork(work.RootElement, workId, doc?.ValueKind == JsonValueKind.Object ? doc : null);
        }, ct);

    private Task<ResolvedAuthor?> GetAuthorAsync(string authorKey, CancellationToken ct) =>
        cache.GetOrCreateAsync($"openlibrary:author:{authorKey}", ExternalLookupCacheStore.EntityTtl, async token =>
        {
            using var document = await ExternalHttp.GetJsonAsync(httpClient, $"authors/{authorKey}.json", SourceName, token, cooldown: cooldown);
            return document is null ? null : ParseAuthor(document.RootElement, authorKey);
        }, ct);

    internal static IReadOnlyList<ExternalBookCandidate> ParseSearch(JsonElement root) =>
        root.Arr("docs")
            .Select(ParseSearchDoc)
            .OfType<ExternalBookCandidate>()
            .DistinctBy(c => c.ExternalId)
            .ToList();

    internal static ExternalBookCandidate? ParseSearchDoc(JsonElement doc)
    {
        var workId = KeyId(doc.Str("key"));
        var title = doc.Str("title");
        if (workId is null || title is null)
        {
            return null;
        }

        // Search results are works: an ISBN or publisher would belong to one arbitrary edition, so none is reported.
        return new ExternalBookCandidate(
            ExternalProvider.OpenLibrary, workId, title, doc.Str("subtitle"), ParseSearchAuthors(doc),
            null, doc.Int("first_publish_year"), null, null, CoverUrl(doc.Int("cover_i")), null, [], null, workId);
    }

    internal static ExternalBookCandidate? ParseWork(JsonElement work, string workId, JsonElement? searchDoc)
    {
        var title = work.Str("title") ?? searchDoc?.Str("title");
        if (title is null)
        {
            return null;
        }

        var authors = searchDoc is null ? [] : ParseSearchAuthors(searchDoc.Value);
        if (authors.Count == 0)
        {
            authors = work.Arr("authors")
                .Select(a => KeyId(a.Prop("author")?.Str("key")))
                .OfType<string>()
                .Select(key => new ExternalAuthorRef(key, key)) // name resolved later through the author record
                .ToList();
        }

        var cover = work.Arr("covers").Where(c => c.ValueKind == JsonValueKind.Number)
            .Select(c => c.TryGetInt32(out var id) ? id : 0).FirstOrDefault(id => id > 0);

        return new ExternalBookCandidate(
            ExternalProvider.OpenLibrary, workId, title, work.Str("subtitle") ?? searchDoc?.Str("subtitle"), authors,
            null, searchDoc?.Int("first_publish_year"), null, null,
            CoverUrl(cover > 0 ? cover : searchDoc?.Int("cover_i")), TextValue(work, "description"), [], null, workId);
    }

    internal static ExternalBookCandidate? ParseEdition(JsonElement edition, string editionId, ExternalBookCandidate? work)
    {
        var title = edition.Str("title") ?? work?.Title;
        if (title is null)
        {
            return null;
        }

        var isbn13 = edition.Strings("isbn_13").Select(Isbn.Normalize).FirstOrDefault(Isbn.IsValidIsbn13);
        var isbn10 = edition.Strings("isbn_10").Select(Isbn.Normalize).FirstOrDefault(Isbn.IsValidIsbn10);
        var cover = edition.Arr("covers").Where(c => c.ValueKind == JsonValueKind.Number)
            .Select(c => c.TryGetInt32(out var id) ? id : 0).FirstOrDefault(id => id > 0);
        var editionAuthors = edition.Arr("authors")
            .Select(a => KeyId(a.Str("key")))
            .OfType<string>()
            .Select(key => new ExternalAuthorRef(key, key))
            .ToList();

        return new ExternalBookCandidate(
            ExternalProvider.OpenLibrary,
            editionId,
            title,
            edition.Str("subtitle"),
            work?.Authors.Count > 0 ? work.Authors : editionAuthors,
            edition.Strings("publishers").FirstOrDefault(),
            work?.PublicationYear ?? GoogleBooksClient.ParseYear(YearOf(edition.Str("publish_date"))),
            isbn13,
            isbn10,
            CoverUrl(cover > 0 ? cover : null) ?? work?.CoverUrl,
            TextValue(edition, "description") ?? work?.Description,
            [],
            null,
            work?.OpenLibraryWorkId);
    }

    internal static ResolvedAuthor? ParseAuthor(JsonElement author, string authorKey)
    {
        var name = author.Str("name") ?? author.Str("personal_name");
        if (name is null)
        {
            return null;
        }

        var remoteIds = author.Prop("remote_ids");
        var wikidata = remoteIds?.Str("wikidata");
        return new ResolvedAuthor(
            name,
            authorKey,
            wikidata is not null && WikidataQid().IsMatch(wikidata) ? wikidata : null,
            remoteIds?.Str("viaf"),
            ParseBirthDate(author.Str("birth_date")));
    }

    internal static DateOnly? ParseBirthDate(string? value) =>
        value is not null && DateTime.TryParseExact(
            value.Trim(), BirthDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? DateOnly.FromDateTime(date)
            : null;

    private static List<ExternalAuthorRef> ParseSearchAuthors(JsonElement doc)
    {
        var names = doc.Strings("author_name").ToList();
        var keys = doc.Strings("author_key").ToList();
        return names.Select((name, i) => new ExternalAuthorRef(name, i < keys.Count ? keys[i] : null)).ToList();
    }

    /// <summary>"description" is either a string or {"type": "/type/text", "value": "..."}.</summary>
    private static string? TextValue(JsonElement element, string name)
    {
        var value = element.Prop(name);
        var text = value?.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Object => value.Value.Str("value"),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string? YearOf(string? publishDate)
    {
        var match = publishDate is null ? null : Year().Match(publishDate);
        return match is { Success: true } ? match.Value : null;
    }

    private static string? CoverUrl(int? coverId) => coverId is > 0 ? $"{CoverBaseAddress}{coverId}-L.jpg" : null;

    /// <summary>"/works/OL45883W" → "OL45883W".</summary>
    private static string? KeyId(string? key)
    {
        var id = key?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return id is not null && OpenLibraryId().IsMatch(id) ? id : null;
    }

    [GeneratedRegex("^OL[0-9]+[WMA]$")]
    private static partial Regex OpenLibraryId();

    [GeneratedRegex("^Q[0-9]+$")]
    private static partial Regex WikidataQid();

    [GeneratedRegex("[0-9]{4}")]
    private static partial Regex Year();
}
