namespace AtlasLiterario.Domain.Enums;

/// <summary>Provider of an external book identifier (persisted).</summary>
public enum BookIdentifierProvider : short
{
    GoogleBooks = 0,
    OpenLibraryWork = 1,
    OpenLibraryEdition = 2,
    Wikidata = 3,
}
