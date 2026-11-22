namespace AtlasLiterario.Domain.Enums;

/// <summary>Provenance of a geographic relation.</summary>
public enum GeoSource : short
{
    Manual = 0,
    Wikidata = 1,
    OpenLibrary = 2,
    GoogleBooks = 3,
}
