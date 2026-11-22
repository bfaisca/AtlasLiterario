namespace AtlasLiterario.Domain.Enums;

/// <summary>Bibliographic source exposed in the API contracts (ExternalSourceRef). Not persisted.</summary>
public enum ExternalProvider : short
{
    GoogleBooks = 0,
    OpenLibrary = 1,
    Wikidata = 2,
    Local = 3,
}
