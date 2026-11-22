namespace AtlasLiterario.Domain.Enums;

/// <summary>Reading status of a book in a user's library. Only Read counts for maps.</summary>
public enum ReadingStatus : short
{
    WantToRead = 0,
    Reading = 1,
    Read = 2,
    Abandoned = 3,
}
