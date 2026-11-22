using System.Diagnostics;

namespace AtlasLiterario.Application.Common;

/// <summary>Total time a request may spend on external sources (import + geographic enrichment).</summary>
public interface IEnrichmentSettings
{
    TimeSpan Budget { get; }
}

/// <summary>Fallback used when the host does not configure the budget (12 s).</summary>
public sealed class DefaultEnrichmentSettings : IEnrichmentSettings
{
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(12);

    public TimeSpan Budget => DefaultBudget;
}

/// <summary>
/// Time budget of one request. Registered as scoped, so the clock starts when the request's services are created
/// (i.e. at the beginning of the request) and import and enrichment share the same allowance.
/// </summary>
public sealed class RequestBudget(IEnrichmentSettings? settings = null)
{
    private readonly long _start = Stopwatch.GetTimestamp();

    public TimeSpan Total => settings?.Budget ?? DefaultEnrichmentSettings.DefaultBudget;

    public TimeSpan Remaining
    {
        get
        {
            var remaining = Total - Stopwatch.GetElapsedTime(_start);
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }
}
