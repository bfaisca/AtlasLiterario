namespace AtlasLiterario.Infrastructure.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinSecretLength = 32;

    /// <summary>HMAC-SHA256 signing secret. Provided via environment (Jwt__Secret) or user-secrets — never versioned.</summary>
    public string Secret { get; set; } = string.Empty;

    public string Issuer { get; set; } = "atlas-literario";

    public string Audience { get; set; } = "atlas-literario-web";

    public int AccessTokenMinutes { get; set; } = 60;
}
