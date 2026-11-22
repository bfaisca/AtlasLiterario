using AtlasLiterario.Application.Auth;
using AtlasLiterario.Application.Tests.Fakes;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Tests.Auth;

public sealed class AuthServiceTests : IDisposable
{
    private const string Password = "segredo123";

    private readonly AppDbContext _db = TestDb.Create();
    private readonly FakeClock _clock = new();
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeEmailSender _email = new();
    private readonly FakeTokenService _tokens = new();
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _sut = new AuthService(_db, new FakePasswordHasher(), _tokens, _clock, _currentUser, _email);
    }

    public void Dispose() => _db.Dispose();

    private Task<AuthResponse> RegisterAsync(string email = "ana@example.com", string password = Password) =>
        _sut.RegisterAsync(new RegisterRequest("Ana", email, password), CancellationToken.None);

    [Fact]
    public async Task Register_creates_user_with_normalized_email_hashed_password_and_session()
    {
        var response = await RegisterAsync("  Ana@Example.COM ");

        var user = await _db.Users.SingleAsync();
        user.Email.ShouldBe("ana@example.com");
        user.PasswordHash.ShouldBe("hashed:" + Password);
        user.MapVisibility.ShouldBe(MapVisibility.Private);

        response.User.Id.ShouldBe(user.Id);
        response.User.Email.ShouldBe("ana@example.com");
        response.AccessTokenExpiresAt.ShouldBe(_clock.UtcNow.AddMinutes(60));

        var stored = await _db.RefreshTokens.SingleAsync();
        stored.TokenHash.ShouldBe("h:" + response.RefreshToken); // only the hash is persisted
        stored.ExpiresAt.ShouldBe(_clock.UtcNow.AddDays(30));
    }

    [Fact]
    public async Task Register_with_existing_email_in_other_case_is_a_conflict()
    {
        await RegisterAsync("ana@example.com");

        var ex = await Should.ThrowAsync<ConflictException>(() => RegisterAsync("ANA@example.com"));

        ex.Code.ShouldBe("auth.email_taken");
    }

    [Theory]
    [InlineData("curta1")]          // too short
    [InlineData("somenteletras")]   // no digit
    [InlineData("1234567890")]      // no letter
    public async Task Register_rejects_weak_password(string password)
    {
        var ex = await Should.ThrowAsync<ValidationException>(() => RegisterAsync(password: password));

        ex.Code.ShouldBe("validation.failed");
        ex.Errors.ShouldContainKey("password");
        (await _db.Users.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Register_reports_all_invalid_fields()
    {
        var ex = await Should.ThrowAsync<ValidationException>(() =>
            _sut.RegisterAsync(new RegisterRequest("", "not-an-email", "x"), CancellationToken.None));

        ex.Errors.Keys.ShouldBe(["name", "email", "password"], ignoreOrder: true);
    }

    [Fact]
    public async Task Login_returns_new_session_for_valid_credentials()
    {
        await RegisterAsync();

        var response = await _sut.LoginAsync(new LoginRequest("ANA@example.com", Password), CancellationToken.None);

        response.User.Email.ShouldBe("ana@example.com");
        (await _db.RefreshTokens.CountAsync()).ShouldBe(2);
    }

    [Theory]
    [InlineData("ana@example.com", "errada123")]
    [InlineData("ninguem@example.com", Password)]
    [InlineData("", "")]
    public async Task Login_with_invalid_credentials_is_unauthorized(string email, string password)
    {
        await RegisterAsync();

        var ex = await Should.ThrowAsync<UnauthorizedException>(() =>
            _sut.LoginAsync(new LoginRequest(email, password), CancellationToken.None));

        ex.Code.ShouldBe("auth.invalid_credentials");
    }

    [Fact]
    public async Task Login_rehashes_password_when_requested_by_hasher()
    {
        var user = new User("Bia", "bia@example.com", "legacy:" + Password, _clock.UtcNow);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        await _sut.LoginAsync(new LoginRequest("bia@example.com", Password), CancellationToken.None);

        (await _db.Users.SingleAsync(u => u.Id == user.Id)).PasswordHash.ShouldBe("hashed:" + Password);
    }

    [Fact]
    public async Task Refresh_rotates_the_token()
    {
        var registered = await RegisterAsync();
        _clock.Advance(TimeSpan.FromMinutes(5));

        var refreshed = await _sut.RefreshAsync(new RefreshRequest(registered.RefreshToken), CancellationToken.None);

        refreshed.RefreshToken.ShouldNotBe(registered.RefreshToken);
        var old = await _db.RefreshTokens.SingleAsync(t => t.TokenHash == "h:" + registered.RefreshToken);
        old.RevokedAt.ShouldBe(_clock.UtcNow);
        old.ReplacedByTokenHash.ShouldBe("h:" + refreshed.RefreshToken);
        (await _db.RefreshTokens.SingleAsync(t => t.TokenHash == "h:" + refreshed.RefreshToken)).IsActive(_clock.UtcNow)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_revokes_all_sessions()
    {
        var registered = await RegisterAsync();
        var refreshed = await _sut.RefreshAsync(new RefreshRequest(registered.RefreshToken), CancellationToken.None);

        var ex = await Should.ThrowAsync<UnauthorizedException>(() =>
            _sut.RefreshAsync(new RefreshRequest(registered.RefreshToken), CancellationToken.None));

        ex.Code.ShouldBe("auth.invalid_refresh_token");
        (await _db.RefreshTokens.AllAsync(t => t.RevokedAt != null)).ShouldBeTrue();
        await Should.ThrowAsync<UnauthorizedException>(() =>
            _sut.RefreshAsync(new RefreshRequest(refreshed.RefreshToken), CancellationToken.None));
    }

    [Fact]
    public async Task Expired_or_unknown_refresh_token_is_unauthorized()
    {
        var registered = await RegisterAsync();
        _clock.Advance(TimeSpan.FromDays(31));

        await Should.ThrowAsync<UnauthorizedException>(() =>
            _sut.RefreshAsync(new RefreshRequest(registered.RefreshToken), CancellationToken.None));
        await Should.ThrowAsync<UnauthorizedException>(() =>
            _sut.RefreshAsync(new RefreshRequest("does-not-exist"), CancellationToken.None));
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_of_the_current_user()
    {
        var registered = await RegisterAsync();
        _currentUser.UserId = registered.User.Id;

        await _sut.LogoutAsync(new RefreshRequest(registered.RefreshToken), CancellationToken.None);

        (await _db.RefreshTokens.SingleAsync()).RevokedAt.ShouldNotBeNull();
        await Should.ThrowAsync<UnauthorizedException>(() =>
            _sut.RefreshAsync(new RefreshRequest(registered.RefreshToken), CancellationToken.None));
    }

    [Fact]
    public async Task Logout_does_not_revoke_tokens_of_other_users()
    {
        var ana = await RegisterAsync("ana@example.com");
        var bia = await RegisterAsync("bia@example.com");
        _currentUser.UserId = bia.User.Id;

        await _sut.LogoutAsync(new RefreshRequest(ana.RefreshToken), CancellationToken.None);

        (await _db.RefreshTokens.SingleAsync(t => t.UserId == ana.User.Id)).RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Profile_can_be_read_and_updated()
    {
        var registered = await RegisterAsync();
        _currentUser.UserId = registered.User.Id;

        var updated = await _sut.UpdateProfileAsync(
            new UpdateProfileRequest("  Ana Maria ", MapVisibility.LinkOnly), CancellationToken.None);
        var profile = await _sut.GetProfileAsync(CancellationToken.None);

        updated.Name.ShouldBe("Ana Maria");
        profile.MapVisibility.ShouldBe(MapVisibility.LinkOnly);
    }

    [Fact]
    public async Task Profile_requires_authentication()
    {
        await Should.ThrowAsync<UnauthorizedException>(() => _sut.GetProfileAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Change_password_requires_the_current_password()
    {
        var registered = await RegisterAsync();
        _currentUser.UserId = registered.User.Id;

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            _sut.ChangePasswordAsync(new ChangePasswordRequest("errada123", "novaSenha9"), CancellationToken.None));

        ex.Code.ShouldBe("auth.invalid_password");
        ex.Errors.ShouldContainKey("currentPassword");
    }

    [Fact]
    public async Task Change_password_updates_the_hash()
    {
        var registered = await RegisterAsync();
        _currentUser.UserId = registered.User.Id;

        await _sut.ChangePasswordAsync(new ChangePasswordRequest(Password, "novaSenha9"), CancellationToken.None);

        (await _db.Users.SingleAsync()).PasswordHash.ShouldBe("hashed:novaSenha9");
    }

    [Fact]
    public async Task Forgot_password_for_unknown_email_does_nothing_visible()
    {
        await _sut.ForgotPasswordAsync(new ForgotPasswordRequest("ninguem@example.com"), CancellationToken.None);

        _email.Sent.ShouldBeEmpty();
        (await _db.PasswordResetTokens.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Reset_password_flow_changes_password_revokes_sessions_and_is_single_use()
    {
        var registered = await RegisterAsync();
        await _sut.ForgotPasswordAsync(new ForgotPasswordRequest("ANA@example.com"), CancellationToken.None);
        var (email, _, token) = _email.Sent.ShouldHaveSingleItem();
        email.ShouldBe("ana@example.com");

        await _sut.ResetPasswordAsync(new ResetPasswordRequest(token, "novaSenha9"), CancellationToken.None);

        (await _db.Users.SingleAsync()).PasswordHash.ShouldBe("hashed:novaSenha9");
        await Should.ThrowAsync<UnauthorizedException>(() =>
            _sut.RefreshAsync(new RefreshRequest(registered.RefreshToken), CancellationToken.None));
        var reuse = await Should.ThrowAsync<ValidationException>(() =>
            _sut.ResetPasswordAsync(new ResetPasswordRequest(token, "outraSenha9"), CancellationToken.None));
        reuse.Code.ShouldBe("auth.invalid_reset_token");
    }

    [Fact]
    public async Task Reset_token_expires_after_one_hour_and_older_tokens_are_invalidated()
    {
        await RegisterAsync();
        await _sut.ForgotPasswordAsync(new ForgotPasswordRequest("ana@example.com"), CancellationToken.None);
        await _sut.ForgotPasswordAsync(new ForgotPasswordRequest("ana@example.com"), CancellationToken.None);
        var first = _email.Sent[0].Token;
        var second = _email.Sent[1].Token;

        await Should.ThrowAsync<ValidationException>(() =>
            _sut.ResetPasswordAsync(new ResetPasswordRequest(first, "novaSenha9"), CancellationToken.None));

        _clock.Advance(TimeSpan.FromMinutes(61));
        await Should.ThrowAsync<ValidationException>(() =>
            _sut.ResetPasswordAsync(new ResetPasswordRequest(second, "novaSenha9"), CancellationToken.None));
    }
}
