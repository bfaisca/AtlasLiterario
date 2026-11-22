using Microsoft.Extensions.Options;

namespace AtlasLiterario.Infrastructure.ExternalSources;

/// <summary>Adds the Google Books API key as a header (never as a URL parameter, which would appear in request logs).</summary>
public sealed class GoogleBooksApiKeyHandler(IOptions<GoogleBooksOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        GoogleBooksClient.ApplyApiKey(request, options.Value.ApiKey);
        return base.SendAsync(request, cancellationToken);
    }
}
