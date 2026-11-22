using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AtlasLiterario.Infrastructure.ExternalSources;

/// <summary>Cache of external lookups. A miss or a cache failure never breaks the lookup itself.</summary>
public interface IExternalLookupCache
{
    /// <summary>Returns the cached value (null values are cached too) or runs <paramref name="factory"/> and stores its result.</summary>
    Task<T?> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T?>> factory, CancellationToken ct)
        where T : class;
}

/// <summary>
/// Stores parsed (compact) results in <c>external_lookup_cache</c>. Uses its own short-lived DbContext so cache writes
/// never flush (or get flushed with) the caller's pending changes.
/// </summary>
public sealed partial class ExternalLookupCacheStore(
    DbContextOptions<AppDbContext> dbOptions,
    IClock clock,
    ILogger<ExternalLookupCacheStore> logger) : IExternalLookupCache
{
    public static readonly TimeSpan SearchTtl = TimeSpan.FromHours(24);
    public static readonly TimeSpan EntityTtl = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetOrCreateAsync<T>(
        string key, TimeSpan ttl, Func<CancellationToken, Task<T?>> factory, CancellationToken ct)
        where T : class
    {
        var cacheKey = NormalizeKey(key);
        var cached = await TryReadAsync<T>(cacheKey, ct);
        if (cached is not null)
        {
            return cached.Value;
        }

        var value = await factory(ct);
        await TryWriteAsync(cacheKey, new Envelope<T>(value), ttl, ct);
        return value;
    }

    internal static string NormalizeKey(string key)
    {
        var normalized = key.Trim().ToLowerInvariant();
        if (normalized.Length <= ExternalLookupCache.MaxKeyLength)
        {
            return normalized;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return normalized[..(ExternalLookupCache.MaxKeyLength - hash.Length - 1)] + "#" + hash;
    }

    private async Task<Envelope<T>?> TryReadAsync<T>(string key, CancellationToken ct)
    {
        try
        {
            await using var db = new AppDbContext(dbOptions);
            var entry = await db.ExternalLookupCache.AsNoTracking().SingleOrDefaultAsync(e => e.Key == key, ct);
            if (entry is null || entry.IsExpired(clock.UtcNow))
            {
                return null;
            }

            return JsonSerializer.Deserialize<Envelope<T>>(entry.Payload, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCacheFailure(logger, "read", key, ex.Message);
            return null;
        }
    }

    private async Task TryWriteAsync<T>(string key, Envelope<T> value, TimeSpan ttl, CancellationToken ct)
    {
        try
        {
            var now = clock.UtcNow;
            var payload = JsonSerializer.Serialize(value, JsonOptions);
            await using var db = new AppDbContext(dbOptions);
            var entry = await db.ExternalLookupCache.SingleOrDefaultAsync(e => e.Key == key, ct);
            if (entry is null)
            {
                db.ExternalLookupCache.Add(new ExternalLookupCache(key, payload, now, now.Add(ttl)));
            }
            else
            {
                entry.Refresh(payload, now, now.Add(ttl));
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Includes concurrent inserts of the same key (unique primary key).
            LogCacheFailure(logger, "write", key, ex.Message);
        }
    }

    private sealed record Envelope<T>(T? Value);

    [LoggerMessage(Level = LogLevel.Debug, Message = "External lookup cache {Operation} failed for {Key}: {Reason}")]
    private static partial void LogCacheFailure(ILogger logger, string operation, string key, string reason);
}
