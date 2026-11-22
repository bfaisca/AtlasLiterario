using System.Text;
using System.Text.Json;
using AtlasLiterario.Application.ExternalSources;
using AtlasLiterario.Domain.Common;
using AtlasLiterario.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AtlasLiterario.Infrastructure.ExternalSources;

/// <summary>
/// Wikidata geography through the SPARQL endpoint (compact answers: one round-trip returns statements, ranks and P298;
/// entity JSON documents of authors are ~300 KB). Ranks: deprecated statements are ignored; if any statement is
/// preferred, only preferred ones are used. P27 with end time (P582) is kept (historical citizenship is valid).
/// </summary>
public sealed partial class WikidataClient(
    HttpClient httpClient,
    IExternalLookupCache cache,
    ICountryCatalog countryCatalog,
    ILogger<WikidataClient> logger,
    ExternalCooldown? cooldown = null) : IGeoResolver
{
    public const string BaseAddress = "https://query.wikidata.org/";
    public const decimal DirectConfidence = 0.9m;
    public const decimal MappedConfidence = 0.6m;
    private const string SourceName = "Wikidata";
    private const string EntityPrefix = "http://www.wikidata.org/entity/";
    private const string RankPrefix = "http://wikiba.se/ontology#";

    public async Task<IReadOnlyList<ResolvedCountry>> GetAuthorCitizenshipsAsync(string authorQid, CancellationToken ct)
    {
        EnsureQid(authorQid);
        var statements = await cache.GetOrCreateAsync<IReadOnlyList<Statement>>(
            $"wikidata:citizenship:{authorQid}",
            ExternalLookupCacheStore.EntityTtl,
            async token => ParseStatements(await QueryAsync(CitizenshipQuery(authorQid), token), "country"),
            ct) ?? [];

        return await ResolveCountriesAsync(SelectByRank(statements), ct);
    }

    public async Task<IReadOnlyList<ResolvedCountry>> GetWorkOriginsAsync(string workQid, CancellationToken ct)
    {
        var statements = await GetWorkStatementsAsync(workQid, ct);
        return await ResolveCountriesAsync(SelectByRank(statements.Where(s => s.Kind == "origin").ToList()), ct);
    }

    public async Task<string?> GetWorkOriginalLanguageAsync(string workQid, CancellationToken ct)
    {
        var statements = await GetWorkStatementsAsync(workQid, ct);
        var codes = SelectByRank(statements.Where(s => s.Kind == "language").ToList())
            .Select(s => s.Code)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return codes.Count == 1 ? codes[0].ToLowerInvariant() : null; // several languages → ambiguous → unknown
    }

    public async Task<string?> FindWorkQidAsync(
        string? isbn13, string? openLibraryWorkId, string title, string? authorQid, CancellationToken ct)
    {
        // ISBN (P212) is stored hyphenated in Wikidata and only on edition items, so it is not used for lookups.
        if (openLibraryWorkId is not null && OpenLibraryWorkId(openLibraryWorkId))
        {
            var byOpenLibrary = await cache.GetOrCreateAsync<WorkMatch>(
                $"wikidata:findwork:ol:{openLibraryWorkId}",
                ExternalLookupCacheStore.EntityTtl,
                async token => new WorkMatch(SingleQid(await QueryAsync(FindByOpenLibraryQuery(openLibraryWorkId), token))),
                ct);
            if (byOpenLibrary?.Qid is not null)
            {
                return byOpenLibrary.Qid;
            }
        }

        if (authorQid is null || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        EnsureQid(authorQid);
        var byTitle = await cache.GetOrCreateAsync<WorkMatch>(
            $"wikidata:findwork:title:{authorQid}:{TextNormalizer.NormalizeForComparison(title)}",
            ExternalLookupCacheStore.EntityTtl,
            async token => new WorkMatch(SingleQid(await QueryAsync(FindByTitleQuery(authorQid, title), token))),
            ct);
        return byTitle?.Qid;
    }

    private async Task<IReadOnlyList<Statement>> GetWorkStatementsAsync(string workQid, CancellationToken ct)
    {
        EnsureQid(workQid);
        return await cache.GetOrCreateAsync<IReadOnlyList<Statement>>(
            $"wikidata:work:{workQid}",
            ExternalLookupCacheStore.EntityTtl,
            async token => ParseStatements(await QueryAsync(WorkQuery(workQid), token), "value"),
            ct) ?? [];
    }

    private async Task<IReadOnlyList<ResolvedCountry>> ResolveCountriesAsync(IEnumerable<Statement> statements, CancellationToken ct)
    {
        var result = new List<ResolvedCountry>();
        foreach (var group in statements.GroupBy(s => s.Value, StringComparer.Ordinal))
        {
            var p298 = group.Select(s => s.Iso).OfType<string>().ToList();
            var resolved = await countryCatalog.ResolveAsync(group.Key, p298, ct);
            if (resolved.Count == 0)
            {
                LogUnmappedCountry(logger, group.Key, string.Join(",", p298));
                continue;
            }

            result.AddRange(resolved.Select(r => new ResolvedCountry(
                r.IsoCode, GeoSource.Wikidata, r.ViaMapping ? MappedConfidence : DirectConfidence, group.Key, r.ViaMapping)));
        }

        return result
            .GroupBy(r => r.IsoCode, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(r => r.Confidence).First())
            .ToList();
    }

    /// <summary>Ignore deprecated; if any preferred statement exists, keep only preferred ones.</summary>
    internal static IReadOnlyList<Statement> SelectByRank(IReadOnlyCollection<Statement> statements)
    {
        var usable = statements.Where(s => s.Rank != "DeprecatedRank").ToList();
        var preferred = usable.Where(s => s.Rank == "PreferredRank").ToList();
        return preferred.Count > 0 ? preferred : usable;
    }

    internal static IReadOnlyList<Statement> ParseStatements(JsonDocument? document, string valueVariable)
    {
        if (document is null)
        {
            return [];
        }

        return document.RootElement.Prop("results")?.Arr("bindings")
            .Select(binding => new Statement(
                binding.Prop("kind")?.Str("value") ?? "country",
                EntityId(binding.Prop(valueVariable)?.Str("value")) ?? string.Empty,
                binding.Prop("rank")?.Str("value")?.Replace(RankPrefix, string.Empty, StringComparison.Ordinal) ?? "NormalRank",
                binding.Prop("iso")?.Str("value"),
                binding.Prop("code")?.Str("value")))
            .Where(s => s.Value.Length > 0)
            .ToList() ?? [];
    }

    /// <summary>Exactly one distinct work → its QID; zero or several (ambiguous) → null.</summary>
    internal static string? SingleQid(JsonDocument? document)
    {
        var qids = document?.RootElement.Prop("results")?.Arr("bindings")
            .Select(b => EntityId(b.Prop("work")?.Str("value")))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];
        return qids.Count == 1 ? qids[0] : null;
    }

    private async Task<JsonDocument?> QueryAsync(string sparql, CancellationToken ct) =>
        await ExternalHttp.GetJsonAsync(
            httpClient,
            $"sparql?query={Uri.EscapeDataString(sparql)}&format=json",
            SourceName,
            ct,
            accept: "application/sparql-results+json",
            cooldown: cooldown);

    internal static string CitizenshipQuery(string qid) =>
        $"SELECT ?country ?rank ?iso WHERE {{ wd:{qid} p:P27 ?statement . ?statement ps:P27 ?country ; wikibase:rank ?rank . OPTIONAL {{ ?country wdt:P298 ?iso }} }}";

    internal static string WorkQuery(string qid) =>
        "SELECT ?kind ?value ?rank ?iso ?code WHERE { "
        + $"{{ wd:{qid} p:P495 ?statement . ?statement ps:P495 ?value ; wikibase:rank ?rank . OPTIONAL {{ ?value wdt:P298 ?iso }} BIND(\"origin\" AS ?kind) }} UNION "
        + $"{{ wd:{qid} p:P407 ?statement . ?statement ps:P407 ?value ; wikibase:rank ?rank . OPTIONAL {{ ?value wdt:P218 ?code }} BIND(\"language\" AS ?kind) }} UNION "
        + $"{{ wd:{qid} p:P364 ?statement . ?statement ps:P364 ?value ; wikibase:rank ?rank . OPTIONAL {{ ?value wdt:P218 ?code }} BIND(\"language\" AS ?kind) }} }}";

    internal static string FindByOpenLibraryQuery(string workId) =>
        $"SELECT DISTINCT ?work WHERE {{ ?work wdt:P648 \"{workId}\" . }} LIMIT 5";

    /// <summary>Exact label (any language, case-insensitive) + P50 author, excluding edition items (Q3331189).</summary>
    internal static string FindByTitleQuery(string authorQid, string title) =>
        $"SELECT DISTINCT ?work WHERE {{ ?work wdt:P50 wd:{authorQid} ; rdfs:label ?label . "
        + $"FILTER(LCASE(STR(?label)) = \"{EscapeLiteral(title.Trim().ToLowerInvariant())}\") "
        + "MINUS { ?work wdt:P31 wd:Q3331189 } } LIMIT 5";

    internal static string EscapeLiteral(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '\\' => @"\\",
                '"' => "\\\"",
                '\n' => " ",
                '\r' => " ",
                _ => c.ToString(),
            });
        }

        return builder.ToString();
    }

    private static string? EntityId(string? uri) =>
        uri is not null && uri.StartsWith(EntityPrefix, StringComparison.Ordinal) ? uri[EntityPrefix.Length..] : null;

    private static void EnsureQid(string qid)
    {
        if (qid.Length < 2 || qid[0] != 'Q' || !qid.AsSpan(1).ContainsOnlyDigits())
        {
            throw new ArgumentException("Invalid Wikidata QID.", nameof(qid));
        }
    }

    private static bool OpenLibraryWorkId(string id) =>
        id.Length > 3 && id.StartsWith("OL", StringComparison.Ordinal) && id.EndsWith('W') && id.AsSpan(2, id.Length - 3).ContainsOnlyDigits();

    /// <summary>One SPARQL row. Kind: "country" | "origin" | "language".</summary>
    public sealed record Statement(string Kind, string Value, string Rank, string? Iso, string? Code);

    public sealed record WorkMatch(string? Qid);

    [LoggerMessage(Level = LogLevel.Information, Message = "Wikidata country {Qid} (P298: {Codes}) has no ISO 3166-1 match nor mapping; discarded")]
    private static partial void LogUnmappedCountry(ILogger logger, string qid, string codes);
}

internal static class SpanExtensions
{
    public static bool ContainsOnlyDigits(this ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
        {
            return false;
        }

        foreach (var c in span)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
