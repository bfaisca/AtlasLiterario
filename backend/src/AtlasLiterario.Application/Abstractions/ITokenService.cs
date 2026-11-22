using AtlasLiterario.Domain.Entities;

namespace AtlasLiterario.Application.Abstractions;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    /// <summary>Creates a signed JWT access token (claim <c>sub</c> = user id).</summary>
    AccessToken CreateAccessToken(User user, DateTimeOffset now);

    /// <summary>Cryptographically random, URL-safe opaque token (refresh, reset, share).</summary>
    string GenerateSecureToken();

    /// <summary>Deterministic one-way hash used to store opaque tokens.</summary>
    string HashToken(string token);
}
