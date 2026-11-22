namespace AtlasLiterario.Domain.Entities;

/// <summary>Cached raw payload of an external lookup (Google Books, Open Library, Wikidata).</summary>
public sealed class ExternalLookupCache
{
    public const int MaxKeyLength = 300;

    private ExternalLookupCache()
    {
    }

    public ExternalLookupCache(string key, string payload, DateTimeOffset fetchedAt, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > MaxKeyLength)
        {
            throw new ArgumentException("Cache key is too long.", nameof(key));
        }

        Key = key;
        Refresh(payload, fetchedAt, expiresAt);
    }

    public string Key { get; private set; } = null!;

    /// <summary>JSON payload (jsonb).</summary>
    public string Payload { get; private set; } = null!;

    public DateTimeOffset FetchedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    public void Refresh(string payload, DateTimeOffset fetchedAt, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (expiresAt <= fetchedAt)
        {
            throw new ArgumentException("Expiration must be after fetch time.", nameof(expiresAt));
        }

        Payload = payload;
        FetchedAt = fetchedAt;
        ExpiresAt = expiresAt;
    }
}
