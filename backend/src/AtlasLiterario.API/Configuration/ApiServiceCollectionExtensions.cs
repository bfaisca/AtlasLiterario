using AtlasLiterario.API.ErrorHandling;
using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AtlasLiterario.API.Configuration;

public static class ApiServiceCollectionExtensions
{
    public const string CorsOriginsSection = "Cors:AllowedOrigins";

    public static IServiceCollection AddApiPresentation(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        services.AddControllers()
            .AddJsonOptions(o => JsonSettings.Apply(o.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = context =>
            {
                var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
                var problem = factory.CreateValidationProblemDetails(
                    context.HttpContext, context.ModelState, StatusCodes.Status400BadRequest);
                problem.Extensions[ProblemDetailsDefaults.CodeKey] = "validation.failed";
                return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
            });
        // Used by ProblemDetails writing and OpenAPI schema generation.
        services.ConfigureHttpJsonOptions(o => JsonSettings.Apply(o.SerializerOptions));

        services.AddProblemDetails(o => o.CustomizeProblemDetails = ProblemDetailsDefaults.Customize);
        services.AddExceptionHandler<GlobalExceptionHandler>();

        AddJwtAuthentication(services);

        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var origins = configuration.GetSection(CorsOriginsSection).Get<string[]>() ?? [];
            options.AddDefaultPolicy(policy => policy
                .WithOrigins(origins.Where(o => !string.IsNullOrWhiteSpace(o)).ToArray())
                .AllowAnyHeader()
                .AllowAnyMethod());
        });

        services.AddOpenApi(o =>
        {
            o.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            o.AddSchemaTransformer<UtcDateTimeOffsetSchemaTransformer>();
        });
        services.AddHealthChecks();

        return services;
    }

    private static void AddJwtAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var settings = jwtOptions.Value;
                bearer.MapInboundClaims = false; // keep "sub", "email", "name" as issued
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtTokenService.CreateSigningKey(settings.Secret),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                };
            });

        // Every endpoint requires authentication unless it is explicitly marked [AllowAnonymous].
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
    }
}
