using System.Net.Mail;
using AtlasLiterario.Application.Common;
using AtlasLiterario.Domain.Entities;

namespace AtlasLiterario.Application.Auth;

internal static class AuthValidation
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;

    public static void ValidateName(string? name, ValidationErrors errors, string field = "name")
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > User.MaxNameLength)
        {
            errors.Add(field, $"O nome deve ter entre 1 e {User.MaxNameLength} caracteres.");
        }
    }

    public static void ValidateEmail(string? email, ValidationErrors errors, string field = "email")
    {
        if (!IsValidEmail(email))
        {
            errors.Add(field, "Informe um e-mail válido.");
        }
    }

    public static bool IsValidEmail(string? email)
    {
        var trimmed = email?.Trim();
        return !string.IsNullOrEmpty(trimmed)
               && trimmed.Length <= User.MaxEmailLength
               && MailAddress.TryCreate(trimmed, out var address)
               && string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Password policy: 8–128 characters, at least one letter and one digit.</summary>
    public static void ValidatePassword(string? password, ValidationErrors errors, string field = "password")
    {
        if (string.IsNullOrEmpty(password)
            || password.Length < MinPasswordLength
            || password.Length > MaxPasswordLength
            || !password.Any(char.IsLetter)
            || !password.Any(char.IsDigit))
        {
            errors.Add(
                field,
                $"A senha deve ter entre {MinPasswordLength} e {MaxPasswordLength} caracteres, com ao menos uma letra e um número.");
        }
    }
}
