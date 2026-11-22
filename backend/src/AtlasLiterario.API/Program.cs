using AtlasLiterario.API.Configuration;
using AtlasLiterario.Application;
using AtlasLiterario.Infrastructure;
using AtlasLiterario.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddApiPresentation();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages(); // ProblemDetails (with code) for empty-body 401/403/404 responses

app.MapOpenApi().AllowAnonymous(); // /openapi/v1.json — official contract reference
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "Atlas Literário API v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "Atlas Literário API";
});

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
