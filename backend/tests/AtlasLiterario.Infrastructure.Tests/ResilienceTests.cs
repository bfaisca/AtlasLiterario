using System.Diagnostics;
using System.Net;
using AtlasLiterario.Application.Common;
using AtlasLiterario.Infrastructure.ExternalSources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtlasLiterario.Infrastructure.Tests;

/// <summary>The real resilience pipeline (as registered in production) in front of a scripted handler.</summary>
public sealed class ResilienceTests
{
    private sealed class ScriptedHandler(Func<int, HttpResponseMessage> script) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(script(Calls));
        }
    }

    private static HttpClient Pipeline(ScriptedHandler handler, bool retryOn429)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("t", c => c.BaseAddress = new Uri("https://example.test/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddStandardResilienceHandler(o => DependencyInjection.ConfigureResilience(o, TimeSpan.FromSeconds(4), retryOn429));
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("t");
    }

    private static HttpResponseMessage TooMany(int? retryAfterSeconds)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        if (retryAfterSeconds is not null)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds.Value));
        }

        return response;
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    [Fact]
    public async Task Long_retry_after_is_not_waited_for_and_not_retried()
    {
        var handler = new ScriptedHandler(_ => TooMany(120));
        var clock = Stopwatch.StartNew();

        var response = await Pipeline(handler, retryOn429: true).GetAsync("x");

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        handler.Calls.ShouldBe(1);
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Short_retry_after_is_honored_once_and_the_retry_can_succeed()
    {
        var handler = new ScriptedHandler(n => n == 1 ? TooMany(1) : new HttpResponseMessage(HttpStatusCode.OK));
        var clock = Stopwatch.StartNew();

        var response = await Pipeline(handler, retryOn429: true).GetAsync("x");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        handler.Calls.ShouldBe(2);
        clock.Elapsed.ShouldBeInRange(TimeSpan.FromMilliseconds(900), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Google_style_429_is_never_retried()
    {
        var handler = new ScriptedHandler(_ => TooMany(1));

        var response = await Pipeline(handler, retryOn429: false).GetAsync("x");

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_429_pauses_the_source_and_the_next_call_fails_fast_without_hitting_it()
    {
        var handler = new ScriptedHandler(_ => TooMany(120));
        var client = Pipeline(handler, retryOn429: true);
        var cooldown = new ExternalCooldown();

        await Should.ThrowAsync<ExternalServiceUnavailableException>(() => ExternalHttp.GetJsonAsync(client, "a", "Wikidata", default, cooldown: cooldown));
        await Should.ThrowAsync<ExternalServiceUnavailableException>(() => ExternalHttp.GetJsonAsync(client, "b", "Wikidata", default, cooldown: cooldown));

        handler.Calls.ShouldBe(1); // second call never left the process
        cooldown.IsPaused("Wikidata").ShouldBeTrue();
        cooldown.IsPaused("Open Library").ShouldBeFalse();
    }

    [Fact]
    public void Pause_uses_retry_after_clamped_between_one_and_ten_minutes_and_expires()
    {
        var time = new ManualTime();
        var cooldown = new ExternalCooldown(time);

        cooldown.Pause("s", null);
        time.Advance(TimeSpan.FromMinutes(4));
        cooldown.IsPaused("s").ShouldBeTrue();
        time.Advance(TimeSpan.FromMinutes(2));
        cooldown.IsPaused("s").ShouldBeFalse();

        cooldown.Pause("s", TimeSpan.FromHours(5)); // clamped to 10 min
        time.Advance(TimeSpan.FromMinutes(9));
        cooldown.IsPaused("s").ShouldBeTrue();
        time.Advance(TimeSpan.FromMinutes(2));
        cooldown.IsPaused("s").ShouldBeFalse();
    }
}
