using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Domain.Entities;

/// <summary>Relation author → country with provenance (source, confidence, verification).</summary>
public sealed class AuthorCountry
{
    private AuthorCountry()
    {
    }

    public AuthorCountry(
        Guid authorId,
        short countryId,
        AuthorCountryRelationship relationshipType,
        GeoSource source,
        decimal confidence,
        bool isVerified,
        Guid? updatedByUserId,
        DateTimeOffset now)
    {
        AuthorId = authorId;
        CountryId = countryId;
        RelationshipType = relationshipType;
        Source = source;
        Confidence = GeoRelation.ValidateConfidence(confidence);
        IsVerified = isVerified;
        UpdatedByUserId = updatedByUserId;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid AuthorId { get; private set; }

    public short CountryId { get; private set; }

    public AuthorCountryRelationship RelationshipType { get; private set; }

    public GeoSource Source { get; private set; }

    public decimal Confidence { get; private set; }

    public bool IsVerified { get; private set; }

    public Guid? UpdatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Author? Author { get; private set; }

    public Country? Country { get; private set; }

    public bool IsManual => Source == GeoSource.Manual;

    /// <summary>Manual correction: Source=Manual, Confidence=1, verified.</summary>
    public static AuthorCountry CreateManual(
        Guid authorId, short countryId, AuthorCountryRelationship relationshipType, Guid userId, DateTimeOffset now) =>
        new(authorId, countryId, relationshipType, GeoSource.Manual, GeoRelation.ManualConfidence, true, userId, now);
}
