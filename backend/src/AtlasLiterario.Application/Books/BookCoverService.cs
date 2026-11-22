using System.Security.Cryptography;
using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Application.Common;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Books;

/// <summary>Image served by GET /api/books/{id}/cover. <see cref="Data"/> is null when the client's ETag is current (304).</summary>
public sealed record CoverImage(string ContentType, string Sha256, byte[]? Data);

public interface IBookCoverService
{
    /// <summary>Stores/replaces the cover. <paramref name="content"/> is read up to the limit + 1 byte.</summary>
    Task<BookDetailsDto> UploadAsync(Guid bookId, Stream? content, CancellationToken ct);

    /// <summary>Removes the uploaded cover (idempotent).</summary>
    Task<BookDetailsDto> DeleteAsync(Guid bookId, CancellationToken ct);

    /// <summary>Anonymous read. <paramref name="matchingEtags"/> are the (unquoted) If-None-Match values.</summary>
    Task<CoverImage> GetAsync(Guid bookId, IReadOnlyCollection<string> matchingEtags, CancellationToken ct);
}

/// <summary>Cover upload. Same permission policy as other corrections: the book must be in the caller's library.</summary>
public sealed class BookCoverService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IBookService bookService,
    IClock clock) : IBookCoverService
{
    public async Task<BookDetailsDto> UploadAsync(Guid bookId, Stream? content, CancellationToken ct)
    {
        var userId = currentUser.GetRequiredUserId();
        var book = await db.Books.SingleOrDefaultAsync(b => b.Id == bookId, ct) ?? throw BookNotFound();
        await LibraryAccess.EnsureUserHasBookAsync(db, userId, bookId, ct);

        if (content is null)
        {
            throw ValidationException.ForField("validation.failed", "file", "Envie o arquivo no campo 'file'.");
        }

        var data = await ReadLimitedAsync(content, ct);
        if (data.Length == 0)
        {
            throw ValidationException.ForField("validation.failed", "file", "O arquivo está vazio.");
        }

        if (data.Length > BookCover.MaxSizeBytes)
        {
            throw new PayloadTooLargeException("cover.too_large", "A capa deve ter no máximo 2 MB.");
        }

        var contentType = ImageSniffer.Detect(data)
                          ?? throw ValidationException.ForField(
                              "cover.invalid_type", "file", "Formato não suportado. Envie uma imagem JPEG, PNG ou WebP.");

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(data));
        var now = clock.UtcNow;
        var existing = await db.BookCovers.SingleOrDefaultAsync(c => c.BookId == bookId, ct);
        if (existing is null)
        {
            db.BookCovers.Add(new BookCover(bookId, contentType, data, sha256, userId, now));
        }
        else
        {
            existing.Replace(contentType, data, sha256, userId, now);
        }

        book.CoverUrl = BookCover.InternalUrl(bookId, sha256);
        book.Touch(now);
        await db.SaveChangesAsync(ct);
        return await bookService.GetAsync(bookId, ct);
    }

    public async Task<BookDetailsDto> DeleteAsync(Guid bookId, CancellationToken ct)
    {
        var userId = currentUser.GetRequiredUserId();
        var book = await db.Books.SingleOrDefaultAsync(b => b.Id == bookId, ct) ?? throw BookNotFound();
        await LibraryAccess.EnsureUserHasBookAsync(db, userId, bookId, ct);

        var existing = await db.BookCovers.SingleOrDefaultAsync(c => c.BookId == bookId, ct);
        if (existing is not null)
        {
            db.BookCovers.Remove(existing);
            book.CoverUrl = null;
            book.Touch(clock.UtcNow);
            await db.SaveChangesAsync(ct);
        }
        else if (book.CoverUrl is not null && book.CoverUrl.StartsWith(BookCover.InternalPath(bookId), StringComparison.Ordinal))
        {
            book.CoverUrl = null; // dangling internal URL
            await db.SaveChangesAsync(ct);
        }

        return await bookService.GetAsync(bookId, ct);
    }

    public async Task<CoverImage> GetAsync(Guid bookId, IReadOnlyCollection<string> matchingEtags, CancellationToken ct)
    {
        var meta = await db.BookCovers.AsNoTracking()
            .Where(c => c.BookId == bookId)
            .Select(c => new { c.ContentType, c.Sha256 })
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("cover.not_found", "Este livro não tem capa enviada.");

        if (matchingEtags.Contains("*") || matchingEtags.Contains(meta.Sha256, StringComparer.OrdinalIgnoreCase))
        {
            return new CoverImage(meta.ContentType, meta.Sha256, null);
        }

        var data = await db.BookCovers.AsNoTracking()
            .Where(c => c.BookId == bookId)
            .Select(c => c.Data)
            .SingleAsync(ct);
        return new CoverImage(meta.ContentType, meta.Sha256, data);
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream content, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > BookCover.MaxSizeBytes)
            {
                break; // do not read more than limit + a chunk
            }
        }

        return buffer.ToArray();
    }

    private static NotFoundException BookNotFound() => new("book.not_found", "Livro não encontrado no catálogo.");
}

/// <summary>Detects JPEG/PNG/WebP from the leading bytes only (client name and Content-Type are ignored).</summary>
public static class ImageSniffer
{
    public static string? Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}
