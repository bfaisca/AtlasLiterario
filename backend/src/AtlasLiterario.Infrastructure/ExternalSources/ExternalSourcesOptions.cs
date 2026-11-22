namespace AtlasLiterario.Infrastructure.ExternalSources;

public sealed class ExternalSourcesOptions
{
    public const string SectionName = "ExternalSources";

    /// <summary>Contact (e-mail or URL) appended to the User-Agent, required by the Wikimedia User-Agent policy.</summary>
    public string Contact { get; set; } = string.Empty;

    /// <summary>
    /// Total time one request may spend on external sources (import + enrichment), counted from the start of the
    /// request. What is not finished stays Partial/Pending/Failed. Default 12 s (the web client times out at 20 s).
    /// </summary>
    public double EnrichmentBudgetSeconds { get; set; } = 12;

    public string BuildUserAgent() =>
        string.IsNullOrWhiteSpace(Contact) ? "AtlasLiterario/0.1" : $"AtlasLiterario/0.1 ({Contact})";
}

public sealed class GoogleBooksOptions
{
    public const string SectionName = "GoogleBooks";

    /// <summary>
    /// Optional API key (GoogleBooks__ApiKey). Sent in the <c>x-goog-api-key</c> header — never in the URL — so it
    /// cannot leak through request logs.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 sent as <c>country</c>; required when Google cannot geolocate the server IP.</summary>
    public string Country { get; set; } = "BR";
}

/// <summary>Adapts <see cref="ExternalSourcesOptions.EnrichmentBudgetSeconds"/> to the Application setting.</summary>
public sealed class EnrichmentSettings(Microsoft.Extensions.Options.IOptionsMonitor<ExternalSourcesOptions> options)
    : AtlasLiterario.Application.Common.IEnrichmentSettings
{
    public TimeSpan Budget =>
        TimeSpan.FromSeconds(Math.Clamp(options.CurrentValue.EnrichmentBudgetSeconds, 0.5, 60));
}
