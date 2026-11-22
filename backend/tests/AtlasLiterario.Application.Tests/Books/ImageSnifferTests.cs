using AtlasLiterario.Application.Books;

namespace AtlasLiterario.Application.Tests.Books;

public sealed class ImageSnifferTests
{
    [Fact]
    public void Detects_only_jpeg_png_and_webp_from_the_leading_bytes()
    {
        ImageSniffer.Detect([0xFF, 0xD8, 0xFF, 0xE0]).ShouldBe("image/jpeg");
        ImageSniffer.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]).ShouldBe("image/png");
        ImageSniffer.Detect("RIFF\u0010\0\0\0WEBPVP8 "u8).ShouldBe("image/webp");
    }

    [Theory]
    [InlineData("GIF89a")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'/>")]
    [InlineData("%PDF-1.4")]
    [InlineData("RIFF\u0010\0\0\0WAVEfmt ")]
    [InlineData("")]
    public void Rejects_everything_else(string content) =>
        ImageSniffer.Detect(System.Text.Encoding.UTF8.GetBytes(content)).ShouldBeNull();

    [Fact]
    public void Truncated_signatures_are_rejected()
    {
        ImageSniffer.Detect([0xFF, 0xD8]).ShouldBeNull();
        ImageSniffer.Detect([0x89, 0x50, 0x4E, 0x47]).ShouldBeNull();
        ImageSniffer.Detect("RIFF\0\0\0\0WEB"u8).ShouldBeNull();
    }
}
