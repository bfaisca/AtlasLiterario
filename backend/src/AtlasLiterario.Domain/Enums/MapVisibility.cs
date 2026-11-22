namespace AtlasLiterario.Domain.Enums;

/// <summary>Privacy setting of the user's maps.</summary>
public enum MapVisibility : short
{
    Private = 0,
    LinkOnly = 1,

    /// <summary>Discoverable profile; maps (countries, authors, titles, reviews) visible to any authenticated user. Explicit opt-in only.</summary>
    Public = 2,
}
