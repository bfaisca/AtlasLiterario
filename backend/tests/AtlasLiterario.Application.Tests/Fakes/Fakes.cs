using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Exceptions;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Tests.Fakes;

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2025, 3, 14, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>Reversible fake hash ("hashed:" prefix). "legacy:" hashes verify but request a rehash.</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => "hashed:" + password;

    public PasswordVerificationResult Verify(string passwordHash, string providedPassword) =>
        passwordHash == "hashed:" + providedPassword ? PasswordVerificationResult.Success
        : passwordHash == "legacy:" + providedPassword ? PasswordVerificationResult.SuccessRehashNeeded
        : PasswordVerificationResult.Failed;
}

internal sealed class FakeTokenService : ITokenService
{
    private int _counter;

    public AccessToken CreateAccessToken(User user, DateTimeOffset now) =>
        new($"access-{user.Id}-{++_counter}", now.AddMinutes(60));

    public string GenerateSecureToken() => $"token-{++_counter}";

    public string HashToken(string token) => "h:" + token;
}

internal sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }

    public bool IsAuthenticated => UserId.HasValue;

    public Guid GetRequiredUserId() => UserId ?? throw new UnauthorizedException("auth.unauthorized", "anonymous");
}

internal sealed class FakeEmailSender : IEmailSender
{
    public List<(string Email, string Name, string Token)> Sent { get; } = [];

    public Task SendPasswordResetAsync(string toEmail, string displayName, string resetToken, CancellationToken cancellationToken)
    {
        Sent.Add((toEmail, displayName, resetToken));
        return Task.CompletedTask;
    }
}

internal static class TestDb
{
    public static AppDbContext Create() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"atlas-tests-{Guid.NewGuid()}")
            .Options);
}
