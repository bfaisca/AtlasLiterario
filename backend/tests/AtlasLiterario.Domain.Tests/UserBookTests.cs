using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;

namespace AtlasLiterario.Domain.Tests;

public sealed class UserBookTests
{
    private static readonly DateTimeOffset Now = new(2025, 3, 14, 18, 20, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2025, 3, 14);

    private static UserBook Create(
        ReadingStatus status,
        int? rating = null,
        DateOnly? startDate = null,
        DateOnly? finishedDate = null,
        string? review = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), status, rating, review, startDate, finishedDate, Now);

    [Fact]
    public void Creating_as_read_without_finished_date_uses_today_utc()
    {
        var userBook = Create(ReadingStatus.Read);

        userBook.FinishedDate.ShouldBe(Today);
    }

    [Fact]
    public void Creating_as_read_keeps_the_given_finished_date()
    {
        var finished = new DateOnly(2024, 12, 1);

        var userBook = Create(ReadingStatus.Read, finishedDate: finished);

        userBook.FinishedDate.ShouldBe(finished);
    }

    [Theory]
    [InlineData(ReadingStatus.WantToRead)]
    [InlineData(ReadingStatus.Reading)]
    [InlineData(ReadingStatus.Abandoned)]
    public void Finished_date_is_ignored_when_status_is_not_read(ReadingStatus status)
    {
        var userBook = Create(status, finishedDate: new DateOnly(2024, 12, 1));

        userBook.FinishedDate.ShouldBeNull();
    }

    [Fact]
    public void Changing_status_to_read_fills_finished_date()
    {
        var userBook = Create(ReadingStatus.Reading);

        userBook.ChangeStatus(ReadingStatus.Read, Now.AddDays(2));

        userBook.Status.ShouldBe(ReadingStatus.Read);
        userBook.FinishedDate.ShouldBe(Today.AddDays(2));
        userBook.UpdatedAt.ShouldBe(Now.AddDays(2));
    }

    [Fact]
    public void Leaving_read_clears_finished_date()
    {
        var userBook = Create(ReadingStatus.Read, finishedDate: new DateOnly(2024, 12, 1));

        userBook.ChangeStatus(ReadingStatus.Reading, Now);

        userBook.FinishedDate.ShouldBeNull();
    }

    [Fact]
    public void Changing_read_to_read_keeps_existing_finished_date()
    {
        var finished = new DateOnly(2024, 12, 1);
        var userBook = Create(ReadingStatus.Read, finishedDate: finished);

        userBook.ChangeStatus(ReadingStatus.Read, Now.AddDays(10));

        userBook.FinishedDate.ShouldBe(finished);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Rating_outside_1_to_5_is_rejected(int rating)
    {
        var ex = Should.Throw<ValidationException>(() => Create(ReadingStatus.Read, rating: rating));

        ex.Code.ShouldBe("library.invalid_rating");
        ex.Errors.ShouldContainKey("rating");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Rating_within_range_is_accepted(int rating)
    {
        Create(ReadingStatus.Read, rating: rating).Rating.ShouldBe((short)rating);
    }

    [Fact]
    public void Start_date_after_finished_date_is_rejected()
    {
        var ex = Should.Throw<ValidationException>(() => Create(
            ReadingStatus.Read, startDate: new DateOnly(2025, 2, 1), finishedDate: new DateOnly(2025, 1, 1)));

        ex.Code.ShouldBe("library.invalid_dates");
    }

    [Fact]
    public void Start_date_equal_to_finished_date_is_accepted()
    {
        var date = new DateOnly(2025, 1, 1);

        var userBook = Create(ReadingStatus.Read, startDate: date, finishedDate: date);

        userBook.StartDate.ShouldBe(date);
        userBook.FinishedDate.ShouldBe(date);
    }

    [Fact]
    public void Auto_filled_finished_date_before_start_date_is_rejected()
    {
        var userBook = Create(ReadingStatus.Reading, startDate: Today.AddDays(5));

        Should.Throw<ValidationException>(() => userBook.ChangeStatus(ReadingStatus.Read, Now))
            .Code.ShouldBe("library.invalid_dates");
    }

    [Fact]
    public void Review_longer_than_limit_is_rejected()
    {
        var review = new string('a', UserBook.MaxReviewLength + 1);

        Should.Throw<ValidationException>(() => Create(ReadingStatus.Read, review: review))
            .Code.ShouldBe("library.review_too_long");
    }

    [Fact]
    public void Blank_review_is_stored_as_null()
    {
        Create(ReadingStatus.Read, review: "   ").Review.ShouldBeNull();
    }

    [Fact]
    public void Undefined_status_is_rejected()
    {
        Should.Throw<ValidationException>(() => Create((ReadingStatus)42)).Code.ShouldBe("library.invalid_status");
    }
}
