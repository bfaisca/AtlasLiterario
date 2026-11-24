using AtlasLiterario.API.Configuration;
using AtlasLiterario.Application;
using AtlasLiterario.Infrastructure;
using AtlasLiterario.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddApiPresentation();

var app = builder.Build();

if (app.Configuration.GetValue(ApiServiceCollectionExtensions.ForwardedHeadersSetting, false))
{
    app.UseForwardedHeaders(); // first: everything after (rate limiter, HTTPS checks) must see the real client
}

app.UseExceptionHandler();
app.UseStatusCodePages(); // ProblemDetails (with code) for empty-body 401/403/404 responses

app.MapOpenApi().AllowAnonymous(); // /openapi/v1.json — official contract reference
// The interactive UI is on by default everywhere except Production; Swagger:UiEnabled overrides it.
// The contract itself (/openapi/v1.json) stays available, since it is the official reference.
if (app.Configuration.GetValue<bool?>(ApiServiceCollectionExtensions.SwaggerUiSetting) ?? !app.Environment.IsProduction())
{
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Atlas Literário API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "Atlas Literário API";
    });
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter(); // after authentication: the profile-search limit is per user

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

await app.Services.InitializeDatabaseAsync();
await app.RunAsync();

/// <summary>Entry point; partial so integration tests can reference it (WebApplicationFactory&lt;Program&gt;).</summary>
public partial class Program
{
}
