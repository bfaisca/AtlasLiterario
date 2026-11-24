using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace AtlasLiterario.API.IntegrationTests;

/// <summary>Production hardening: auth rate limit per client IP, forwarded headers and Swagger UI exposure.</summary>
public sealed class DeploymentHardeningTests : IClassFixture<AtlasApiFactory>
{
    private readonly AtlasApiFactory _factory;

    public DeploymentHardeningTests(AtlasApiFactory factory) => _factory = factory;

    private HttpClient CreateClient(Dictionary<string, string?> settings, string? environment = null) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            if (environment is not null)
            {
                builder.UseEnvironment(environment);
            }
        }).CreateClient();

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "ninguem@example.com", password = "senha-errada-123" }),
        };
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return client.SendAsync(request);
    }

    [Fact]
    public async Task Login_is_limited_per_ip_and_answers_429_with_retry_after()
    {
        var client = CreateClient(new() { ["RateLimiting:AuthPerMinute"] = "3" });

        for (var i = 0; i < 3; i++)
        {
            (await LoginAsync(client)).StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }

        var limited = await LoginAsync(client);
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact]
    public async Task Register_and_password_reset_share_the_same_credential_limit()
    {
        var client = CreateClient(new() { ["RateLimiting:AuthPerMinute"] = "2" });

        await LoginAsync(client);
        await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "ninguem@example.com" });

        (await client.PostAsJsonAsync("/api/auth/register", new { name = "x", email = "x@example.com", password = "senha-forte-123" }))
            .StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Forwarded_for_is_ignored_unless_enabled()
    {
        var client = CreateClient(new() { ["RateLimiting:AuthPerMinute"] = "1" });

        await LoginAsync(client, "203.0.113.10");

        // Spoofing another address must not give a fresh budget when forwarded headers are off.
        (await LoginAsync(client, "203.0.113.11")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Forwarded_for_separates_clients_behind_a_proxy_when_enabled()
    {
        var client = CreateClient(new()
        {
            ["RateLimiting:AuthPerMinute"] = "1",
            ["ForwardedHeaders:Enabled"] = "true",
        });

        await LoginAsync(client, "203.0.113.10");

        (await LoginAsync(client, "203.0.113.10")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await LoginAsync(client, "203.0.113.11")).StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Swagger_ui_is_off_in_production_but_the_contract_stays_available()
    {
        var client = CreateClient([], "Production");

        // Anonymous requests to an unmapped route fall to the authenticated-by-default policy (401): the UI is not served.
        (await client.GetAsync("/swagger/index.html")).StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
        (await client.GetAsync("/openapi/v1.json")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Swagger_ui_can_be_forced_on_in_production_and_is_on_outside_it()
    {
        var forced = CreateClient(new() { ["Swagger:UiEnabled"] = "true" }, "Production");
        (await forced.GetAsync("/swagger/index.html")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var development = CreateClient([], "Development");
        (await development.GetAsync("/swagger/index.html")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
