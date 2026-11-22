using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;

namespace AtlasLiterario.Domain.Entities;

public sealed class User
{
    public const int MaxNameLength = 100;
    public const int MaxEmailLength = 254;

    private User()
    {
    }

    public User(string name, string email, string passwordHash, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        Name = ValidateName(name);
        Email = NormalizeEmail(email);
        if (Email.Length is 0 or > MaxEmailLength)
        {
            throw ValidationException.ForField("user.invalid_email", "email", "E-mail inválido.");
        }

        PasswordHash = RequireHash(passwordHash);
        MapVisibility = MapVisibility.Private;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>Always stored trimmed and lower-cased (case-insensitive uniqueness).</summary>
    public string Email { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    public MapVisibility MapVisibility { get; private set; }

    /// <summary>Defaults to <see cref="UserRole.User"/>. Changed only by operators (SQL script), never by an API endpoint.</summary>
    public UserRole Role { get; private set; } = UserRole.User;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static string NormalizeEmail(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public void UpdateProfile(string name, MapVisibility mapVisibility, DateTimeOffset now)
    {
        if (!Enum.IsDefined(mapVisibility))
        {
            throw ValidationException.ForField("user.invalid_map_visibility", "mapVisibility", "Visibilidade inválida.");
        }

        Name = ValidateName(name);
        MapVisibility = mapVisibility;
        UpdatedAt = now;
    }

    public void SetRole(UserRole role, DateTimeOffset now)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        Role = role;
        UpdatedAt = now;
    }

    public void ChangePasswordHash(string passwordHash, DateTimeOffset now)
    {
        PasswordHash = RequireHash(passwordHash);
        UpdatedAt = now;
    }

    private static string ValidateName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > MaxNameLength)
        {
            throw ValidationException.ForField(
                "user.invalid_name", "name", $"O nome deve ter entre 1 e {MaxNameLength} caracteres.");
        }

        return trimmed;
    }

    private static string RequireHash(string passwordHash) =>
        string.IsNullOrWhiteSpace(passwordHash)
            ? throw new ArgumentException("Password hash is required.", nameof(passwordHash))
            : passwordHash;
}
