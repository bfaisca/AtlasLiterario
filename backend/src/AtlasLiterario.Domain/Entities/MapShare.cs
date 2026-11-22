using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Domain.Entities;

/// <summary>Optional public link exposing only the aggregated data of one map.</summary>
public sealed class MapShare
{
    public const int TokenLength = 43;

    private MapShare()
    {
    }

    public MapShare(Guid userId, MapType mapType, string token, DateTimeOffset now)
    {
        if (token is not { Length: TokenLength })
        {
            throw new ArgumentException($"Token must have {TokenLength} characters.", nameof(token));
        }

        Id = Guid.CreateVersion7();
        UserId = userId;
        MapType = mapType;
        Token = token;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public MapType MapType { get; private set; }

    public string Token { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
