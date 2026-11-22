using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AtlasLiterario.Application.ExternalSources;
using AtlasLiterario.Domain.Common;
using AtlasLiterario.Domain.Enums;
using Microsoft.Extensions.Options;

namespace AtlasLiterario.Infrastructure.ExternalSources;

/// <summary>Google Books (bibliographic identification). Edition language is never used as original language.</summary>
public sealed partial class GoogleBooksClient(
    HttpClient httpClient,
    IOptions<GoogleBooksOptions> options,
    IExternalLookupCache cache,
    ExternalCooldown? cooldown = null) : IBookSearchProvider
{
    public const string BaseAddress = "https://www.googleapis.com/books/v1/";
    public const string ApiKeyHeader = "x-goog-api-key";
    private const string SourceName = "Google Books";
    private const int MaxResults = 40;

    private readonly GoogleBooksOptions _options = options.Value;

    public ExternalProvider Provider => ExternalProvider.GoogleBooks;

    public async Task<IReadOnlyList<ExternalBookCandidate>> SearchAsync(BookSearchQuery query, CancellationToken ct)
    {
        var term = query.Type switch
        {
            BookSearchType.Title => "intitle:" + query.Term,
            BookSearchType.Author => "inauthor:" + query.Term,
            BookSearchType.Isbn => "isbn:" + query.Term,
            _ => query.Term,
        };
        var limit = Math.Clamp(query.Limit, 1, MaxResults);
        var uri = $"volumes?q={Uri.EscapeDataString(term)}&maxResults={limit}&printType=books{CountryParameter()}";

        return await cache.GetOrCreateAsync<IReadOnlyList<ExternalBookCandidate>>(
            $"googlebooks:search:{query.Type}:{query.Term}:{limit}",
            ExternalLookupCacheStore.SearchTtl,
            async token =>
            {
                using var document = await ExternalHttp.GetJsonAsync(httpClient, uri, SourceName, token, cooldown: cooldown) ;
                return document is null ? [] : ParseSearch(document.RootElement);
            },
            ct) ?? [];
    }

    public Task<ExternalBookCandidate?> GetAsync(string externalId, CancellationToken ct) =>
        cache.GetOrCreateAsync(
            $"googlebooks:volume:{externalId}",
            ExternalLookupCacheStore.EntityTtl,
            async token =>
            {
                var uri = $"volumes/{Uri.EscapeDataString(externalId)}?{CountryParameter().TrimStart('&')}";
                using var document = await ExternalHttp.GetJsonAsync(httpClient, uri, SourceName, token, cooldown: cooldown);
                return document is null ? null : ParseVolume(document.RootElement);
            },
            ct);

    /// <summary>Adds the API key header (if configured). Called by the HTTP pipeline for every request.</summary>
    internal static void ApplyApiKey(HttpRequestMessage request, string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.TryAddWithoutValidation(ApiKeyHeader, apiKey);
        }
    }

    private string CountryParameter() =>
        string.IsNullOrWhiteSpace(_options.Country) ? string.Empty : "&country=" + Uri.EscapeDataString(_options.Country);

    internal static IReadOnlyList<ExternalBookCandidate> ParseSearch(JsonElement root) =>
        root.Arr("items")
            .Select(ParseVolume)
            .OfType<ExternalBookCandidate>()
            .DistinctBy(c => c.ExternalId)
            .ToList();

    internal static ExternalBookCandidate? ParseVolume(JsonElement item)
    {
        var id = item.Str("id");
        var info = item.Prop("volumeInfo");
        var title = info?.Str("title");
        if (id is null || info is null || title is null)
        {
            return null;
        }

        var volume = info.Value;
        string? isbn13 = null, isbn10 = null;
        foreach (var identifier in volume.Arr("industryIdentifiers"))
        {
            var value = Isbn.Normalize(identifier.Str("identifier"));
            switch (identifier.Str("type"))
            {
                case "ISBN_13" when Isbn.IsValidIsbn13(value):
                    isbn13 ??= value;
                    break;
                case "ISBN_10" when Isbn.IsValidIsbn10(value):
                    isbn10 ??= value;
                    break;
            }
        }

        var images = volume.Prop("imageLinks");
        var cover = images?.Str("thumbnail") ?? images?.Str("smallThumbnail");

        return new ExternalBookCandidate(
            ExternalProvider.GoogleBooks,
            id,
            title,
            volume.Str("subtitle"),
            volume.Strings("authors").Select(name => new ExternalAuthorRef(name, null)).ToList(),
            volume.Str("publisher"),
            ParseYear(volume.Str("publishedDate")),
            isbn13,
            isbn10,
            NormalizeCoverUrl(cover),
            StripHtml(volume.Str("description")),
            volume.Strings("categories").ToList(),
            volume.Str("language"),
            null);
    }

    internal static int? ParseYear(string? date) =>
        date is { Length: >= 4 } && int.TryParse(date.AsSpan(0, 4), out var year) && year is > 0 and < 3000 ? year : null;

    /// <summary>Forces https and removes the page-curl effect (docs/external-integrations.md §1).</summary>
    internal static string? NormalizeCoverUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var normalized = url.Replace("&edge=curl", string.Empty, StringComparison.Ordinal);
        return normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? "https://" + normalized["http://".Length..]
            : normalized;
    }

    internal static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var withBreaks = BreakTags().Replace(ParagraphEnds().Replace(html, "\n\n"), "\n");
        var text = WebUtility.HtmlDecode(AnyTag().Replace(withBreaks, string.Empty));
        var collapsed = BlankLines().Replace(text, "\n\n").Trim();
        return collapsed.Length == 0 ? null : collapsed;
    }

    [GeneratedRegex(@"<\s*/\s*(p|div)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphEnds();

    [GeneratedRegex(@"<\s*(br|/li)\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakTags();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"\n\s*\n+")]
    private static partial Regex BlankLines();
}
