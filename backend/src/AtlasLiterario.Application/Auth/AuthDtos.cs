using System.Text.Json.Serialization;
using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Application.Auth;

public sealed record RegisterRequest(string Name, string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>
/// <c>Handle</c> omitted = keep the current one; <c>null</c> or blank = remove it (refused while <c>MapVisibility</c> is Public).
/// </summary>
public sealed record UpdateProfileRequest(string Name, MapVisibility MapVisibility)
{
    private string? _handle;

    /// <summary>True when the JSON (or initializer) set <see cref="Handle"/>, even to null.</summary>
    [JsonIgnore]
    public bool HandleProvided { get; private set; }

    /// <summary>Public nickname, normalized to lower-case; <c>^[a-z0-9_]{3,30}$</c>, unique.</summary>
    public string? Handle
    {
        get => _handle;
        init
        {
            _handle = value;
            HandleProvided = true;
        }
    }
}

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    UserProfileDto User);

public sealed record UserProfileDto(
    Guid Id,
    string Name,
    string Email,
    UserRole Role,
    MapVisibility MapVisibility,
    DateTimeOffset CreatedAt,
    string? Handle,
    int FollowersCount,
    int FollowingCount);
