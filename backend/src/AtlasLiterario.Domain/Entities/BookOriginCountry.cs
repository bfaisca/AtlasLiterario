using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Domain.Entities;

/// <summary>Literary country of origin of a work, with provenance.</summary>
public sealed class BookOriginCountry
{
    private BookOriginCountry()
    {
    }

    public BookOriginCountry(
        Guid bookId,
        short countryId,
        GeoSource source,
        decimal confidence,
        bool isVerified,
        Guid? updatedByUserId,
        DateTimeOffset now)
    {
        BookId = bookId;
        CountryId = countryId;
        Source = source;
        Confidence = GeoRelation.ValidateConfidence(confidence);
        IsVerified = isVerified;
        UpdatedByUserId = updatedByUserId;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid BookId { get; private set; }

    public short CountryId { get; private set; }

    public GeoSource Source { get; private set; }

    public decimal Confidence { get; private set; }

    public bool IsVerified { get; private set; }

    public Guid? UpdatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Book? Book { get; private set; }

    public Country? Country { get; private set; }

    public bool IsManual => Source == GeoSource.Manual;

    public static BookOriginCountry CreateManual(Guid bookId, short countryId, Guid userId, DateTimeOffset now) =>
        new(bookId, countryId, GeoSource.Manual, GeoRelation.ManualConfidence, true, userId, now);
}
