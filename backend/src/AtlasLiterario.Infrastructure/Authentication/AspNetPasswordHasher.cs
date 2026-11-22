using AtlasLiterario.Application.Abstractions;
using Microsoft.AspNetCore.Identity;
using AppPasswordVerificationResult = AtlasLiterario.Application.Abstractions.PasswordVerificationResult;
using IdentityPasswordVerificationResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult;

namespace AtlasLiterario.Infrastructure.Authentication;

/// <summary>PBKDF2 hashing through ASP.NET Core Identity's PasswordHasher (without adopting full Identity).</summary>
public sealed class AspNetPasswordHasher : IPasswordHasher
{
    // The default PasswordHasher implementation does not use the user instance.
    private static readonly object HashSubject = new();
    private readonly PasswordHasher<object> _inner = new();

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        return _inner.HashPassword(HashSubject, password);
    }

    public AppPasswordVerificationResult Verify(string passwordHash, string providedPassword)
    {
        if (string.IsNullOrEmpty(passwordHash) || providedPassword is null)
        {
            return AppPasswordVerificationResult.Failed;
        }

        try
        {
            return _inner.VerifyHashedPassword(HashSubject, passwordHash, providedPassword) switch
            {
                IdentityPasswordVerificationResult.Success => AppPasswordVerificationResult.Success,
                IdentityPasswordVerificationResult.SuccessRehashNeeded => AppPasswordVerificationResult.SuccessRehashNeeded,
                _ => AppPasswordVerificationResult.Failed,
            };
        }
        catch (FormatException)
        {
            return AppPasswordVerificationResult.Failed;
        }
    }
}
