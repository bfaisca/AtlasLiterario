using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Application.Auth;

public sealed record RegisterRequest(string Name, string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record UpdateProfileRequest(string Name, MapVisibility MapVisibility);

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
    DateTimeOffset CreatedAt);
