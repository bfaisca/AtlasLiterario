namespace AtlasLiterario.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct);

    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct);

    /// <summary>Rotates the refresh token. Reuse of a rotated token revokes every session of the user.</summary>
    Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct);

    /// <summary>Revokes the given refresh token of the current user (idempotent).</summary>
    Task LogoutAsync(RefreshRequest request, CancellationToken ct);

    Task<UserProfileDto> GetProfileAsync(CancellationToken ct);

    Task<UserProfileDto> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct);

    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct);

    /// <summary>Never reveals whether the e-mail exists.</summary>
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct);

    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct);
}
