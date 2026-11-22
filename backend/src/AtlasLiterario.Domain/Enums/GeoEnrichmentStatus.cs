namespace AtlasLiterario.Domain.Enums;

/// <summary>State of the automatic geographic enrichment of a book.</summary>
public enum GeoEnrichmentStatus : short
{
    Pending = 0,
    Completed = 1,
    Partial = 2,
    Failed = 3,
}
