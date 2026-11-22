using System.Net;
using System.Text;

namespace AtlasLiterario.Infrastructure.Tests.Support;

/// <summary>
/// Serves recorded responses from Fixtures/ by matching the request (path + decoded query).
/// Unmatched requests fail the test loudly (404 with a marker body would hide mistakes).
/// </summary>
public sealed class FixtureHttpHandler : HttpMessageHandler
{
    private readonly List<(Func<string, bool> Match, HttpStatusCode Status, string? File)> _routes = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    public FixtureHttpHandler On(string contains, string file, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes.Add((url => url.Contains(contains, StringComparison.Ordinal), status, file));
        return this;
    }

    public FixtureHttpHandler OnStatus(string contains, HttpStatusCode status)
    {
        _routes.Add((url => url.Contains(contains, StringComparison.Ordinal), status, null));
        return this;
    }

    public int CountRequests(string contains) => Requests.Count(r => Describe(r).Contains(contains, StringComparison.Ordinal));

    public static string Describe(HttpRequestMessage request) => Uri.UnescapeDataString(request.RequestUri!.PathAndQuery);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var url = Describe(request);
        var route = _routes.FirstOrDefault(r => r.Match(url));
        if (route.Match is null)
        {
            throw new InvalidOperationException($"No fixture for request: {url}");
        }

        var response = new HttpResponseMessage(route.Status);
        if (route.File is not null)
        {
            response.Content = new StringContent(Fixture.Read(route.File), Encoding.UTF8, "application/json");
        }

        return Task.FromResult(response);
    }
}

public static class Fixture
{
    public static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));
}
