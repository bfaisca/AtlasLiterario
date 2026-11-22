using AtlasLiterario.Domain.Common;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Exceptions;

namespace AtlasLiterario.Domain.Tests;

public sealed class IsbnTests
{
    [Theory]
    [InlineData("9780306406157")]
    [InlineData("978-3-16-148410-0")]
    [InlineData("978 85 359 0277 8")]
    public void Valid_isbn13_is_accepted_after_normalization(string raw)
    {
        Isbn.IsValidIsbn13(Isbn.Normalize(raw)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("9780306406158")] // wrong check digit
    [InlineData("978030640615")]  // 12 digits
    [InlineData("97803064061X7")]
    public void Invalid_isbn13_is_rejected(string raw)
    {
        Isbn.IsValidIsbn13(Isbn.Normalize(raw)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("0-306-40615-2", "9780306406157")]
    [InlineData("080442957X", "9780804429573")]
    [InlineData("080442957x", "9780804429573")]
    public void Isbn10_is_converted_to_isbn13(string isbn10, string expected)
    {
        Isbn.ToIsbn13(isbn10).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0804429573")] // invalid ISBN-10 check digit
    [InlineData("abc")]
    public void ToIsbn13_returns_null_for_invalid_input(string? raw)
    {
        Isbn.ToIsbn13(raw).ShouldBeNull();
    }

    [Fact]
    public void Book_derives_isbn13_from_isbn10()
    {
        var book = new Book("Dom Casmurro", DateTimeOffset.UtcNow);

        book.SetIsbns(null, "0-306-40615-2");

        book.Isbn13.ShouldBe("9780306406157");
        book.Isbn10.ShouldBe("0306406152");
    }

    [Fact]
    public void Book_rejects_invalid_isbn13()
    {
        var book = new Book("Dom Casmurro", DateTimeOffset.UtcNow);

        Should.Throw<ValidationException>(() => book.SetIsbns("9780306406158", null)).Code.ShouldBe("book.invalid_isbn13");
    }
}
