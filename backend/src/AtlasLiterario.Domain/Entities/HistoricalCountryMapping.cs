namespace AtlasLiterario.Domain.Entities;

/// <summary>
/// Maps a Wikidata entity without an ISO alpha-3 code (historical state, kingdom, etc.) to a current country.
/// A QID may map to more than one country (one row per country).
/// </summary>
public sealed class HistoricalCountryMapping
{
    private HistoricalCountryMapping()
    {
    }

    public HistoricalCountryMapping(int id, string wikidataQid, short countryId, string? note)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wikidataQid);
        Id = id;
        WikidataQid = wikidataQid;
        CountryId = countryId;
        Note = note;
    }

    public int Id { get; private set; }

    public string WikidataQid { get; private set; } = null!;

    public short CountryId { get; private set; }

    public string? Note { get; private set; }

    public Country? Country { get; private set; }
}
