using AtlasLiterario.API.ErrorHandling;
using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AtlasLiterario.API.Configuration;

public static class ApiServiceCollectionExtensions
{
    public const string ProfileSearchPolicy = "profile-search";
    public const string ProfileSearchLimitSetting = "RateLimiting:ProfileSearchPerMinute";
    public const int DefaultProfileSearchPerMinute = 30;

    public const string AuthPolicy = "auth";
    public const string AuthLimitSetting = "RateLimiting:AuthPerMinute";
    public const int DefaultAuthPerMinute = 30;

    public const string ForwardedHeadersSetting = "ForwardedHeaders:Enabled";
    public const string SwaggerUiSetting = "Swagger:UiEnabled";

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
        AddRateLimiting(services);

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
        AddForwardedHeaders(services);

        return services;
    }

    /// <summary>
    /// Rate limits (ASP.NET Core's built-in limiter, no extra dependency): profile search per authenticated user and
    /// credential endpoints per client IP.
    /// Profile search is the only discovery endpoint over other users, so it is limited per authenticated user
    /// (fixed window, <see cref="DefaultProfileSearchPerMinute"/> requests/minute; override with <see cref="ProfileSearchLimitSetting"/>).
    /// No extra dependency: ASP.NET Core's built-in rate limiter.
    /// </summary>
    private static void AddRateLimiting(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(ProfileSearchPolicy, httpContext =>
            {
                var limit = httpContext.RequestServices.GetRequiredService<IConfiguration>()
                    .GetValue(ProfileSearchLimitSetting, DefaultProfileSearchPerMinute);
                var user = httpContext.User.FindFirst(HttpContextCurrentUser.SubjectClaim)?.Value ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(user, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
            // Credential endpoints (login, register, password reset) are limited per client IP. Behind a proxy the IP
            // is only the real one when ForwardedHeaders:Enabled is set (see docs/deployment.md).
            options.AddPolicy(AuthPolicy, httpContext =>
            {
                var limit = httpContext.RequestServices.GetRequiredService<IConfiguration>()
                    .GetValue(AuthLimitSetting, DefaultAuthPerMinute);
                var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
            options.OnRejected = async (context, ct) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                }

                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await http.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = { Status = StatusCodes.Status429TooManyRequests, Title = "Muitas requisições.", Detail = "Aguarde um instante e tente novamente." },
                });
            };
        });
    }

    /// <summary>
    /// Behind Render/Vercel the API only sees the proxy. When <see cref="ForwardedHeadersSetting"/> is true the
    /// X-Forwarded-For/-Proto headers are honored (the proxy network is not known in advance, so the allow-lists are
    /// cleared and only the nearest hop is trusted). Off by default: locally nobody should be able to spoof the IP.
    /// </summary>
    private static void AddForwardedHeaders(IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            if (configuration.GetValue(ForwardedHeadersSetting, false))
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            }
        });
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
