namespace AtlasLiterario.Domain.Entities;

/// <summary>Cover image uploaded by a user (one per book, stored in PostgreSQL as bytea).</summary>
public sealed class BookCover
{
    public const int MaxSizeBytes = 2 * 1024 * 1024;

    private BookCover()
    {
    }

    public BookCover(Guid bookId, string contentType, byte[] data, string sha256, Guid? updatedByUserId, DateTimeOffset now)
    {
        BookId = bookId;
        Replace(contentType, data, sha256, updatedByUserId, now);
    }

    public Guid BookId { get; private set; }

    /// <summary>image/jpeg | image/png | image/webp (validated from the bytes, never from the client).</summary>
    public string ContentType { get; private set; } = null!;

    public byte[] Data { get; private set; } = null!;

    public int SizeBytes { get; private set; }

    /// <summary>Lowercase hex SHA-256 of <see cref="Data"/> (cache key and ETag).</summary>
    public string Sha256 { get; private set; } = null!;

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? UpdatedByUserId { get; private set; }

    public void Replace(string contentType, byte[] data, string sha256, Guid? updatedByUserId, DateTimeOffset now)
    {
        if (data is not { Length: > 0 and <= MaxSizeBytes })
        {
            throw new ArgumentException($"Cover must have between 1 and {MaxSizeBytes} bytes.", nameof(data));
        }

        ContentType = contentType;
        Data = data;
        SizeBytes = data.Length;
        Sha256 = sha256;
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = now;
    }

    /// <summary>Internal relative URL stored in <c>book.cover_url</c> (works behind nginx / the Vite proxy).</summary>
    public static string InternalUrl(Guid bookId, string sha256) => $"{InternalPath(bookId)}?v={sha256[..12]}";

    public static string InternalPath(Guid bookId) => $"/api/books/{bookId}/cover";
}
