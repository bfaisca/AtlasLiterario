namespace AtlasLiterario.Domain.Enums;

/// <summary>Provider of an external author identifier (persisted).</summary>
public enum AuthorIdentifierProvider : short
{
    OpenLibrary = 0,
    Wikidata = 1,
    Viaf = 2,
}
