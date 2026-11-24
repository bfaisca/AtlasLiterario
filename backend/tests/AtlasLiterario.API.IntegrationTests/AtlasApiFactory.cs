using AtlasLiterario.Application.Common;
using AtlasLiterario.Application.ExternalSources;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AtlasLiterario.API.IntegrationTests;

/// <summary>
/// Hosts the real API pipeline with EF Core InMemory instead of PostgreSQL.
/// Provider-specific behavior (unique indexes, check constraints, text[]/jsonb, migrations) is NOT covered here;
/// it requires PostgreSQL (planned: Testcontainers when a Docker daemon is available).
/// </summary>
public sealed class AtlasApiFactory : WebApplicationFactory<Program>
{
    /// <summary>How the fake Wikidata behaves (per factory instance, so test classes do not interfere).</summary>
    public GeoMode GeoMode { get; set; } = GeoMode.Normal;

    /// <summary>Request budget for import + enrichment; default 12 s like production.</summary>
    public TimeSpan Budget { get; set; } = TimeSpan.FromSeconds(12);

    /// <summary>Number of Wikidata calls started (to assert that a cooled-down source is not hammered).</summary>
    public int GeoCalls => _geoCalls;

    private int _geoCalls;

    /// <summary>When true Open Library (and Google, always over quota) fail: only the local catalog can answer.</summary>
    public bool ExternalSearchDown { get; set; }

    internal void CountGeoCall() => Interlocked.Increment(ref _geoCalls);

    private readonly string _databaseName = $"atlas-api-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "integration-tests-secret-with-at-least-32-chars",
            ["ConnectionStrings:Default"] = "Host=unused",
            ["Database:ApplyMigrationsOnStartup"] = "false",
            ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
            ["RateLimiting:AuthPerMinute"] = "100000", // every test shares one client IP; DeploymentHardeningTests lowers it
        }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_databaseName));

            // External sources are faked: integration tests never hit the network.
            services.RemoveAll<IBookSearchProvider>();
            services.RemoveAll<IAuthorIdentityResolver>();
            services.RemoveAll<IGeoResolver>();
            services.AddSingleton<IBookSearchProvider>(new SwitchableProvider(this, FakeExternalSources.OpenLibrary));
            services.AddSingleton<IBookSearchProvider>(FakeExternalSources.GoogleBooks);
            services.AddSingleton<IAuthorIdentityResolver>(FakeExternalSources.AuthorResolver);
            services.AddSingleton<IGeoResolver>(new ControllableGeo(this));
            services.RemoveAll<IEnrichmentSettings>();
            services.AddSingleton<IEnrichmentSettings>(new FactoryEnrichmentSettings(this));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated(); // applies HasData seed
        return host;
    }
}

public enum GeoMode
{
    Normal,

    /// <summary>Wikidata answers 429 immediately (Retry-After 120): the client gives up without waiting.</summary>
    RateLimited,

    /// <summary>Wikidata never answers (until the request is cancelled).</summary>
    Hang,
}

internal sealed class FactoryEnrichmentSettings(AtlasApiFactory factory) : IEnrichmentSettings
{
    public TimeSpan Budget => factory.Budget;
}

internal sealed class ControllableGeo(AtlasApiFactory factory) : IGeoResolver
{
    private readonly IGeoResolver _normal = FakeExternalSources.Geo;

    public async Task<IReadOnlyList<ResolvedCountry>> GetAuthorCitizenshipsAsync(string authorQid, CancellationToken ct)
    {
        await GateAsync(ct);
        return await _normal.GetAuthorCitizenshipsAsync(authorQid, ct);
    }


    public async Task<IReadOnlyList<ResolvedCountry>> GetWorkOriginsAsync(string workQid, CancellationToken ct)
    {
        await GateAsync(ct);
        return await _normal.GetWorkOriginsAsync(workQid, ct);
    }


    public async Task<string?> GetWorkOriginalLanguageAsync(string workQid, CancellationToken ct)
    {
        await GateAsync(ct);
        return await _normal.GetWorkOriginalLanguageAsync(workQid, ct);
    }


    public async Task<string?> FindWorkQidAsync(string? isbn13, string? openLibraryWorkId, string title, string? authorQid, CancellationToken ct)
    {
        await GateAsync(ct);
        return await _normal.FindWorkQidAsync(isbn13, openLibraryWorkId, title, authorQid, ct);
    }


    private async Task GateAsync(CancellationToken ct)
    {
        factory.CountGeoCall();
        switch (factory.GeoMode)
        {
            case GeoMode.RateLimited:
                throw new ExternalServiceUnavailableException("Wikidata", "Wikidata respondeu HTTP 429.");
            case GeoMode.Hang:
                await Task.Delay(Timeout.Infinite, ct);
                break;
        }
    }
}

internal sealed class SwitchableProvider(AtlasApiFactory factory, IBookSearchProvider inner) : IBookSearchProvider
{
    public ExternalProvider Provider => inner.Provider;

    public Task<IReadOnlyList<ExternalBookCandidate>> SearchAsync(BookSearchQuery query, CancellationToken ct) =>
        factory.ExternalSearchDown
            ? throw new ExternalServiceUnavailableException("Open Library", "HTTP 503")
            : inner.SearchAsync(query, ct);

    public Task<ExternalBookCandidate?> GetAsync(string externalId, CancellationToken ct) =>
        factory.ExternalSearchDown
            ? throw new ExternalServiceUnavailableException("Open Library", "HTTP 503")
            : inner.GetAsync(externalId, ct);
}
