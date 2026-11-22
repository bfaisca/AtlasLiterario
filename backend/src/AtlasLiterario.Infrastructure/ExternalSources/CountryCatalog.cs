using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Infrastructure.ExternalSources;

/// <summary>Country table + historical mappings, loaded once per scope, used to turn Wikidata countries into ISO codes.</summary>
public interface ICountryCatalog
{
    /// <summary>
    /// 1) QID of a seeded country; 2) P298 present in the country table; 3) historical/explicit mapping (reduced confidence).
    /// Empty when nothing matches — the country is then discarded (never guessed).
    /// </summary>
    Task<IReadOnlyList<(string IsoCode, bool ViaMapping)>> ResolveAsync(string countryQid, IReadOnlyCollection<string> p298Codes, CancellationToken ct);
}

public sealed class CountryCatalog(AppDbContext db) : ICountryCatalog
{
    private Snapshot? _snapshot;

    public async Task<IReadOnlyList<(string IsoCode, bool ViaMapping)>> ResolveAsync(
        string countryQid, IReadOnlyCollection<string> p298Codes, CancellationToken ct)
    {
        var snapshot = _snapshot ??= await LoadAsync(ct);

        if (snapshot.IsoByQid.TryGetValue(countryQid, out var iso))
        {
            return [(iso, false)];
        }

        var known = p298Codes.Where(snapshot.IsoCodes.Contains).Distinct(StringComparer.Ordinal).ToList();
        if (known.Count > 0)
        {
            return known.Select(k => (k, false)).ToList();
        }

        return snapshot.MappedIsos[countryQid].Select(m => (m, true)).ToList();
    }

    private async Task<Snapshot> LoadAsync(CancellationToken ct)
    {
        var countries = await db.Countries.AsNoTracking()
            .Select(c => new { c.IsoCode, c.WikidataQid })
            .ToListAsync(ct);
        var mappings = await db.HistoricalCountryMappings.AsNoTracking()
            .Select(m => new { m.WikidataQid, m.Country!.IsoCode })
            .ToListAsync(ct);

        return new Snapshot(
            countries.Where(c => c.WikidataQid != null).ToDictionary(c => c.WikidataQid!, c => c.IsoCode, StringComparer.Ordinal),
            countries.Select(c => c.IsoCode).ToHashSet(StringComparer.Ordinal),
            mappings.ToLookup(m => m.WikidataQid, m => m.IsoCode, StringComparer.Ordinal));
    }

    private sealed record Snapshot(
        Dictionary<string, string> IsoByQid,
        HashSet<string> IsoCodes,
        ILookup<string, string> MappedIsos);
}
