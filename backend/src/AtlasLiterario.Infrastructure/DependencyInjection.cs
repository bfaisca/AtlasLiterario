using System.Net;
using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Application.ExternalSources;
using AtlasLiterario.Infrastructure.Authentication;
using AtlasLiterario.Infrastructure.Email;
using AtlasLiterario.Infrastructure.ExternalSources;
using AtlasLiterario.Infrastructure.Persistence;
using AtlasLiterario.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace AtlasLiterario.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    /// <summary>
    /// Registers persistence, security and external-source services. Configuration is read lazily
    /// (options pattern), so hosts and tests can override it after this call.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>()
                .GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is not configured (ConnectionStrings__{ConnectionStringName}).");
            }

            options.UseNpgsql(connectionString, npgsql => npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .Validate(o => o.Secret.Length >= JwtOptions.MinSecretLength,
                $"Jwt:Secret must have at least {JwtOptions.MinSecretLength} characters (set Jwt__Secret or user-secrets).")
            .Validate(o => o.AccessTokenMinutes is > 0 and <= 1440, "Jwt:AccessTokenMinutes must be between 1 and 1440.")
            .ValidateOnStart();
        services.AddOptions<AppOptions>().BindConfiguration(AppOptions.SectionName);
        services.AddOptions<ExternalSourcesOptions>().BindConfiguration(ExternalSourcesOptions.SectionName);
        services.AddOptions<GoogleBooksOptions>().BindConfiguration(GoogleBooksOptions.SectionName);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IEmailSender, LoggingEmailSender>();

        AddExternalSources(services);
        return services;
    }

    private static void AddExternalSources(IServiceCollection services)
    {
        services.AddSingleton<ExternalCooldown>();
        services.AddSingleton<AtlasLiterario.Application.Common.IEnrichmentSettings, EnrichmentSettings>();
        services.AddScoped<IExternalLookupCache, ExternalLookupCacheStore>();
        services.AddScoped<ICountryCatalog, CountryCatalog>();
        services.AddTransient<GoogleBooksApiKeyHandler>();

        // Typed clients registered by concrete type (distinct HttpClient names), exposed through the interfaces.
        services.AddHttpClient<GoogleBooksClient>(ConfigureClient(GoogleBooksClient.BaseAddress))
            .AddHttpMessageHandler<GoogleBooksApiKeyHandler>()
            .AddStandardResilienceHandler(o => ConfigureResilience(o, TimeSpan.FromSeconds(4), retryOn429: false));
        services.AddHttpClient<OpenLibraryClient>(ConfigureClient(OpenLibraryClient.BaseAddress))
            .AddStandardResilienceHandler(o => ConfigureResilience(o, TimeSpan.FromSeconds(4), retryOn429: true));
        services.AddHttpClient<WikidataClient>(ConfigureClient(WikidataClient.BaseAddress))
            .AddStandardResilienceHandler(o => ConfigureResilience(o, TimeSpan.FromSeconds(5), retryOn429: true));

        services.AddTransient<IBookSearchProvider>(sp => sp.GetRequiredService<GoogleBooksClient>());
        services.AddTransient<IBookSearchProvider>(sp => sp.GetRequiredService<OpenLibraryClient>());
        services.AddTransient<IAuthorIdentityResolver>(sp => sp.GetRequiredService<OpenLibraryClient>());
        services.AddTransient<IGeoResolver>(sp => sp.GetRequiredService<WikidataClient>());
    }

    private static Action<IServiceProvider, HttpClient> ConfigureClient(string baseAddress) =>
        (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ExternalSourcesOptions>>().Value;
            client.BaseAddress = new Uri(baseAddress);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.BuildUserAgent());
        };

    /// <summary>Longest Retry-After honored by the retry strategy; anything above is not waited for (no retry).</summary>
    internal static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Per-attempt timeout (4 s; SPARQL 5 s) and 1 retry on 5xx/timeout. A 429 is retried (Open Library, Wikidata) only when
    /// Retry-After is absent or ≤ 2 s, waiting at most 2 s; a longer Retry-After is not waited for (the caller degrades and the
    /// source is paused by <see cref="ExternalCooldown"/>). Google Books 429 (quota) is never retried.
    /// The sum of attempts stays inside the request budget (ExternalSources:EnrichmentBudgetSeconds, 12 s).
    /// </summary>
    internal static void ConfigureResilience(HttpStandardResilienceOptions options, TimeSpan attemptTimeout, bool retryOn429)
    {
        options.AttemptTimeout.Timeout = attemptTimeout;
        options.Retry.MaxRetryAttempts = 1;
        options.Retry.Delay = TimeSpan.FromMilliseconds(500);
        options.Retry.UseJitter = false;
        options.Retry.ShouldRetryAfterHeader = false;
        options.TotalRequestTimeout.Timeout = attemptTimeout * 2 + MaxRetryAfter;
        options.CircuitBreaker.SamplingDuration = attemptTimeout * 2 > TimeSpan.FromSeconds(30)
            ? attemptTimeout * 2
            : TimeSpan.FromSeconds(30);
        options.Retry.ShouldHandle = args =>
        {
            if (args.Outcome.Result is { StatusCode: HttpStatusCode.TooManyRequests } response)
            {
                var retryAfter = ExternalHttp.RetryAfter(response);
                return ValueTask.FromResult(retryOn429 && (retryAfter is null || retryAfter <= MaxRetryAfter));
            }

            return ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(args.Outcome));
        };
        options.Retry.DelayGenerator = args =>
        {
            if (args.Outcome.Result is { StatusCode: HttpStatusCode.TooManyRequests } response)
            {
                var wait = ExternalHttp.RetryAfter(response) ?? TimeSpan.FromSeconds(1);
                return ValueTask.FromResult<TimeSpan?>(wait > MaxRetryAfter ? MaxRetryAfter : wait);
            }

            return ValueTask.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(500));
        };
    }
}
