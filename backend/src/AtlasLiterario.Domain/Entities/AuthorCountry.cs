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

    /// <summary>Copy of this relation for another author (used when merging duplicate authors); provenance is preserved.</summary>
    public AuthorCountry CopyFor(Guid authorId) =>
        new(authorId, CountryId, RelationshipType, Source, Confidence, IsVerified, UpdatedByUserId, CreatedAt)
        {
            UpdatedAt = UpdatedAt,
        };

    /// <summary>
    /// Takes over the provenance of <paramref name="other"/> (same country and relationship) when it is better:
    /// verified beats unverified; with the same verification, higher confidence wins.
    /// </summary>
    /// <returns><c>true</c> when this relation was replaced.</returns>
    public bool AdoptIfBetter(AuthorCountry other, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(other);
        var better = other.IsVerified != IsVerified ? other.IsVerified : other.Confidence > Confidence;
        if (!better)
        {
            return false;
        }

        Source = other.Source;
        Confidence = other.Confidence;
        IsVerified = other.IsVerified;
        UpdatedByUserId = other.UpdatedByUserId;
        UpdatedAt = now;
        return true;
    }

    /// <summary>Manual correction: Source=Manual, Confidence=1, verified.</summary>
    public static AuthorCountry CreateManual(
        Guid authorId, short countryId, AuthorCountryRelationship relationshipType, Guid userId, DateTimeOffset now) =>
        new(authorId, countryId, relationshipType, GeoSource.Manual, GeoRelation.ManualConfidence, true, userId, now);
}
