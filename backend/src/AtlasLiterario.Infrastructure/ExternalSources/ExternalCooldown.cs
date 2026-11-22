using System.Collections.Concurrent;

namespace AtlasLiterario.Infrastructure.ExternalSources;

/// <summary>
/// Short negative cache for rate-limited sources: after an HTTP 429 the source is not called again for a few minutes
/// (fail fast instead of hammering it and burning the request budget). Singleton, in memory, per process.
/// </summary>
public sealed class ExternalCooldown(TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan DefaultPause = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MinPause = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxPause = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _until = new(StringComparer.Ordinal);

    public bool IsPaused(string source) =>
        _until.TryGetValue(source, out var until) && until > _time.GetUtcNow();

    /// <summary>Pauses <paramref name="source"/> for the Retry-After (clamped to 1–10 min) or 5 min when absent.</summary>
    public void Pause(string source, TimeSpan? retryAfter)
    {
        var pause = retryAfter is null ? DefaultPause : TimeSpan.FromTicks(Math.Clamp(retryAfter.Value.Ticks, MinPause.Ticks, MaxPause.Ticks));
        _until[source] = _time.GetUtcNow().Add(pause);
    }
}
