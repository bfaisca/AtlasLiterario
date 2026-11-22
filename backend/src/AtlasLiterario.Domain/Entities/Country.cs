using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Domain.Entities;

/// <summary>ISO 3166-1 country/territory. <see cref="IsoCode"/> (alpha-3) is the geographic identifier.</summary>
public sealed class Country
{
    private Country()
    {
    }

    public Country(
        short id,
        string name,
        string namePt,
        string isoCode,
        string isoCode2,
        Continent continent,
        string geoJsonId,
        string? wikidataQid)
    {
        if (isoCode is not { Length: 3 })
        {
            throw new ArgumentException("ISO alpha-3 code must have 3 characters.", nameof(isoCode));
        }

        if (isoCode2 is not { Length: 2 })
        {
            throw new ArgumentException("ISO alpha-2 code must have 2 characters.", nameof(isoCode2));
        }

        Id = id;
        Name = name;
        NamePt = namePt;
        IsoCode = isoCode;
        IsoCode2 = isoCode2;
        Continent = continent;
        GeoJsonId = geoJsonId;
        WikidataQid = wikidataQid;
    }

    /// <summary>ISO 3166-1 numeric code (Kosovo, not in ISO, uses 900).</summary>
    public short Id { get; private set; }

    /// <summary>English name.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>Portuguese (pt-BR) name.</summary>
    public string NamePt { get; private set; } = null!;

    /// <summary>ISO 3166-1 alpha-3.</summary>
    public string IsoCode { get; private set; } = null!;

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string IsoCode2 { get; private set; } = null!;

    public Continent Continent { get; private set; }

    /// <summary>Feature id in the frontend GeoJSON (= ISO_A3).</summary>
    public string GeoJsonId { get; private set; } = null!;

    public string? WikidataQid { get; private set; }
}
