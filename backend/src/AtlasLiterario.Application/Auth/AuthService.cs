using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Application.Common;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Auth;

public sealed class AuthService(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IClock clock,
    ICurrentUser currentUser,
    IEmailSender emailSender) : IAuthService
{
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        AuthValidation.ValidateName(request.Name, errors);
        AuthValidation.ValidateEmail(request.Email, errors);
        AuthValidation.ValidatePassword(request.Password, errors);
        errors.ThrowIfAny();

        var email = User.NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            throw EmailTaken();
        }

        var now = clock.UtcNow;
        var user = new User(request.Name, email, passwordHasher.Hash(request.Password), now);
        db.Users.Add(user);
        var (response, _) = IssueTokens(user, now);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Concurrent registration hit the unique index on e-mail.
            throw EmailTaken();
        }

        return response;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            throw InvalidCredentials();
        }

        var email = User.NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            throw InvalidCredentials();
        }

        var verification = passwordHasher.Verify(user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            throw InvalidCredentials();
        }

        var now = clock.UtcNow;
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.ChangePasswordHash(passwordHasher.Hash(request.Password), now);
        }

        var (response, _) = IssueTokens(user, now);
        await db.SaveChangesAsync(ct);
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw InvalidRefreshToken();
        }

        var hash = tokenService.HashToken(request.RefreshToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null)
        {
            throw InvalidRefreshToken();
        }

        var now = clock.UtcNow;
        if (token.IsRevoked)
        {
            if (token.ReplacedByTokenHash is not null)
            {
                // A rotated token was presented again: possible theft. Revoke every session of the user.
                await RevokeAllRefreshTokensAsync(token.UserId, now, ct);
                await db.SaveChangesAsync(ct);
            }

            throw InvalidRefreshToken();
        }

        if (!token.IsActive(now))
        {
            throw InvalidRefreshToken();
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (user is null)
        {
            throw InvalidRefreshToken();
        }

        var (response, newToken) = IssueTokens(user, now);
        token.Revoke(now, newToken.TokenHash);
        await db.SaveChangesAsync(ct);
        return response;
    }

    public async Task LogoutAsync(RefreshRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = currentUser.GetRequiredUserId();
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return;
        }

        var hash = tokenService.HashToken(request.RefreshToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash && t.UserId == userId, ct);
        if (token is null || token.IsRevoked)
        {
            return;
        }

        token.Revoke(clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public async Task<UserProfileDto> GetProfileAsync(CancellationToken ct)
    {
        var user = await GetCurrentUserAsync(ct);
        return ToProfile(user);
    }

    public async Task<UserProfileDto> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        AuthValidation.ValidateName(request.Name, errors);
        if (!Enum.IsDefined(request.MapVisibility))
        {
            errors.Add("mapVisibility", "Visibilidade inválida.");
        }

        errors.ThrowIfAny();

        var user = await GetCurrentUserAsync(ct);
        user.UpdateProfile(request.Name, request.MapVisibility, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return ToProfile(user);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        AuthValidation.ValidatePassword(request.NewPassword, errors, "newPassword");
        errors.ThrowIfAny();

        var user = await GetCurrentUserAsync(ct);
        if (string.IsNullOrEmpty(request.CurrentPassword)
            || passwordHasher.Verify(user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            throw ValidationException.ForField("auth.invalid_password", "currentPassword", "Senha atual incorreta.");
        }

        user.ChangePasswordHash(passwordHasher.Hash(request.NewPassword), clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!AuthValidation.IsValidEmail(request.Email))
        {
            return;
        }

        var email = User.NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            return;
        }

        var now = clock.UtcNow;

        // Only the most recent reset link is valid.
        var pending = await db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null)
            .ToListAsync(ct);
        foreach (var previous in pending)
        {
            previous.MarkUsed(now);
        }

        var plainToken = tokenService.GenerateSecureToken();
        db.PasswordResetTokens.Add(new PasswordResetToken(
            user.Id, tokenService.HashToken(plainToken), now, now.Add(PasswordResetToken.DefaultLifetime)));
        await db.SaveChangesAsync(ct);

        await emailSender.SendPasswordResetAsync(user.Email, user.Name, plainToken, ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        AuthValidation.ValidatePassword(request.NewPassword, errors, "newPassword");
        errors.ThrowIfAny();

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            throw InvalidResetToken();
        }

        var now = clock.UtcNow;
        var hash = tokenService.HashToken(request.Token);
        var resetToken = await db.PasswordResetTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (resetToken is null || !resetToken.IsUsable(now))
        {
            throw InvalidResetToken();
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == resetToken.UserId, ct)
                   ?? throw InvalidResetToken();

        user.ChangePasswordHash(passwordHasher.Hash(request.NewPassword), now);
        resetToken.MarkUsed(now);

        // Password recovery may follow a compromise: end every existing session.
        await RevokeAllRefreshTokensAsync(user.Id, now, ct);
        await db.SaveChangesAsync(ct);
    }

    private (AuthResponse Response, RefreshToken RefreshToken) IssueTokens(User user, DateTimeOffset now)
    {
        var accessToken = tokenService.CreateAccessToken(user, now);
        var plainRefreshToken = tokenService.GenerateSecureToken();
        var refreshToken = new RefreshToken(
            user.Id, tokenService.HashToken(plainRefreshToken), now, now.Add(RefreshTokenLifetime));
        db.RefreshTokens.Add(refreshToken);

        var response = new AuthResponse(accessToken.Token, accessToken.ExpiresAt, plainRefreshToken, ToProfile(user));
        return (response, refreshToken);
    }

    private async Task RevokeAllRefreshTokensAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var activeTokens = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var activeToken in activeTokens)
        {
            activeToken.Revoke(now);
        }
    }

    private async Task<User> GetCurrentUserAsync(CancellationToken ct)
    {
        var userId = currentUser.GetRequiredUserId();
        return await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct)
               ?? throw new NotFoundException("auth.user_not_found", "Usuário não encontrado.");
    }

    internal static UserProfileDto ToProfile(User user) =>
        new(user.Id, user.Name, user.Email, user.Role, user.MapVisibility, user.CreatedAt);

    private static ConflictException EmailTaken() =>
        new("auth.email_taken", "Já existe uma conta com este e-mail.");

    private static UnauthorizedException InvalidCredentials() =>
        new("auth.invalid_credentials", "E-mail ou senha inválidos.");

    private static UnauthorizedException InvalidRefreshToken() =>
        new("auth.invalid_refresh_token", "Sessão expirada ou inválida. Faça login novamente.");

    private static ValidationException InvalidResetToken() =>
        new("auth.invalid_reset_token", "Link de redefinição inválido ou expirado.");
}
