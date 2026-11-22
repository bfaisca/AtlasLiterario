using AtlasLiterario.Domain.Common;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;

namespace AtlasLiterario.Domain.Entities;

/// <summary>Global bibliographic record, shared by all users (independent of reading experience).</summary>
public sealed class Book
{
    public const int MaxTitleLength = 500;

    private Book()
    {
    }

    public Book(string title, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        SetTitle(title);
        GeoEnrichmentStatus = GeoEnrichmentStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public string Title { get; private set; } = null!;

    public string? Subtitle { get; set; }

    /// <summary>Normalized, check-digit-validated ISBN-13 (deduplication key).</summary>
    public string? Isbn13 { get; private set; }

    public string? Isbn10 { get; private set; }

    public string? Publisher { get; set; }

    public short? PublicationYear { get; set; }

    public string? Description { get; set; }

    public string? CoverUrl { get; set; }

    /// <summary>BCP-47/ISO 639 language of the original work (never the edition language).</summary>
    public string? OriginalLanguage { get; set; }

    public List<string> Genres { get; set; } = [];

    public GeoEnrichmentStatus GeoEnrichmentStatus { get; set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public ICollection<BookAuthor> Authors { get; private set; } = new List<BookAuthor>();

    public ICollection<BookOriginCountry> OriginCountries { get; private set; } = new List<BookOriginCountry>();

    public ICollection<ExternalBookIdentifier> ExternalIdentifiers { get; private set; } = new List<ExternalBookIdentifier>();

    public void SetTitle(string title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > MaxTitleLength)
        {
            throw ValidationException.ForField(
                "book.invalid_title", "title", $"O título deve ter entre 1 e {MaxTitleLength} caracteres.");
        }

        Title = trimmed;
    }

    /// <summary>
    /// Validates and stores ISBNs. When only a valid ISBN-10 is given, the ISBN-13 is derived from it.
    /// </summary>
    public void SetIsbns(string? isbn13, string? isbn10)
    {
        var normalized13 = Isbn.Normalize(isbn13);
        if (normalized13 is not null && !Isbn.IsValidIsbn13(normalized13))
        {
            throw ValidationException.ForField("book.invalid_isbn13", "isbn13", "ISBN-13 inválido.");
        }

        var normalized10 = Isbn.Normalize(isbn10);
        if (normalized10 is not null && !Isbn.IsValidIsbn10(normalized10))
        {
            throw ValidationException.ForField("book.invalid_isbn10", "isbn10", "ISBN-10 inválido.");
        }

        Isbn13 = normalized13 ?? (normalized10 is null ? null : Isbn.ConvertIsbn10To13(normalized10));
        Isbn10 = normalized10;
    }

    public void Touch(DateTimeOffset now) => UpdatedAt = now;
}
