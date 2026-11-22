using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;

namespace AtlasLiterario.Domain.Entities;

/// <summary>The reading experience of one user with one book.</summary>
public sealed class UserBook
{
    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int MaxReviewLength = 10_000;

    private UserBook()
    {
    }

    public UserBook(
        Guid userId,
        Guid bookId,
        ReadingStatus status,
        int? rating,
        string? review,
        DateOnly? startDate,
        DateOnly? finishedDate,
        DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        BookId = bookId;
        CreatedAt = now;
        Update(status, rating, review, startDate, finishedDate, now);
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid BookId { get; private set; }

    public ReadingStatus Status { get; private set; }

    public short? Rating { get; private set; }

    public string? Review { get; private set; }

    public DateOnly? StartDate { get; private set; }

    /// <summary>Only set while <see cref="Status"/> is <see cref="ReadingStatus.Read"/>.</summary>
    public DateOnly? FinishedDate { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Book? Book { get; private set; }

    /// <summary>
    /// Replaces all reading data. When the status is Read and no finished date is given, today (UTC) is used.
    /// When the status is not Read, the finished date is cleared.
    /// </summary>
    public void Update(
        ReadingStatus status,
        int? rating,
        string? review,
        DateOnly? startDate,
        DateOnly? finishedDate,
        DateTimeOffset now)
    {
        EnsureValidStatus(status);
        EnsureValidRating(rating);
        var normalizedReview = NormalizeReview(review);
        var effectiveFinishedDate = ResolveFinishedDate(status, finishedDate, now);
        EnsureDateOrder(startDate, effectiveFinishedDate);

        Status = status;
        Rating = (short?)rating;
        Review = normalizedReview;
        StartDate = startDate;
        FinishedDate = effectiveFinishedDate;
        UpdatedAt = now;
    }

    /// <summary>
    /// Changes only the status: entering Read fills FinishedDate with today (UTC) if empty;
    /// leaving Read clears FinishedDate.
    /// </summary>
    public void ChangeStatus(ReadingStatus status, DateTimeOffset now)
    {
        EnsureValidStatus(status);
        var effectiveFinishedDate = ResolveFinishedDate(status, FinishedDate, now);
        EnsureDateOrder(StartDate, effectiveFinishedDate);

        Status = status;
        FinishedDate = effectiveFinishedDate;
        UpdatedAt = now;
    }

    private static DateOnly? ResolveFinishedDate(ReadingStatus status, DateOnly? requested, DateTimeOffset now) =>
        status == ReadingStatus.Read
            ? requested ?? DateOnly.FromDateTime(now.UtcDateTime)
            : null;

    private static void EnsureValidStatus(ReadingStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw ValidationException.ForField("library.invalid_status", "status", "Status de leitura inválido.");
        }
    }

    private static void EnsureValidRating(int? rating)
    {
        if (rating is < MinRating or > MaxRating)
        {
            throw ValidationException.ForField(
                "library.invalid_rating", "rating", $"A avaliação deve estar entre {MinRating} e {MaxRating}.");
        }
    }

    private static string? NormalizeReview(string? review)
    {
        if (string.IsNullOrWhiteSpace(review))
        {
            return null;
        }

        if (review.Length > MaxReviewLength)
        {
            throw ValidationException.ForField(
                "library.review_too_long", "review", $"A resenha deve ter no máximo {MaxReviewLength} caracteres.");
        }

        return review;
    }

    private static void EnsureDateOrder(DateOnly? startDate, DateOnly? finishedDate)
    {
        if (startDate is not null && finishedDate is not null && startDate > finishedDate)
        {
            throw ValidationException.ForField(
                "library.invalid_dates", "startDate", "A data de início deve ser anterior ou igual à data de conclusão.");
        }
    }
}
