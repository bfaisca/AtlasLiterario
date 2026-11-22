using AtlasLiterario.Domain.Common;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;

namespace AtlasLiterario.Domain.Entities;

public sealed class User
{
    public const int MaxNameLength = 100;
    public const int MaxEmailLength = 254;
    public const int MinHandleLength = 3;
    public const int MaxHandleLength = 30;

    /// <summary>Handles that can never be claimed (routes and institutional names).</summary>
    public static readonly IReadOnlySet<string> ReservedHandles = new HashSet<string>(StringComparer.Ordinal)
    {
        "admin", "atlas", "api", "me", "profiles", "support", "moderator",
    };

    private User()
    {
    }

    public User(string name, string email, string passwordHash, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        Name = ValidateName(name);
        NormalizedName = TextNormalizer.NormalizeForComparison(Name);
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

    /// <summary>Lower-case, accent-free <see cref="Name"/>; used only by the profile search.</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    /// <summary>Unique public nickname (lower-case, <c>^[a-z0-9_]{3,30}$</c>); null until the user chooses one.</summary>
    public string? Handle { get; private set; }

    public string PasswordHash { get; private set; } = null!;

    public MapVisibility MapVisibility { get; private set; }

    /// <summary>Defaults to <see cref="UserRole.User"/>. Changed only by operators (SQL script), never by an API endpoint.</summary>
    public UserRole Role { get; private set; } = UserRole.User;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static string NormalizeEmail(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Trim + lower-case; blank becomes null (no handle).</summary>
    public static string? NormalizeHandle(string? handle)
    {
        var normalized = (handle ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length == 0 ? null : normalized;
    }

    /// <summary>Throws <c>profile.handle_invalid</c> unless <paramref name="handle"/> (already normalized, may be null) is acceptable.</summary>
    public static void ValidateHandle(string? handle)
    {
        if (handle is null)
        {
            return;
        }

        var validFormat = handle.Length is >= MinHandleLength and <= MaxHandleLength
                          && handle.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');
        if (!validFormat)
        {
            throw ValidationException.ForField(
                "profile.handle_invalid", "handle",
                $"O apelido deve ter de {MinHandleLength} a {MaxHandleLength} caracteres: letras minúsculas, números e _.");
        }

        if (ReservedHandles.Contains(handle))
        {
            throw ValidationException.ForField("profile.handle_invalid", "handle", "Este apelido é reservado.");
        }
    }

    /// <summary>Sets (or clears, with null/blank) the handle. Clearing is refused while the profile is public.</summary>
    public void ChangeHandle(string? handle, DateTimeOffset now) => UpdateProfile(Name, MapVisibility, handle, now);

    /// <summary>Keeps the current handle.</summary>
    public void UpdateProfile(string name, MapVisibility mapVisibility, DateTimeOffset now) =>
        UpdateProfile(name, mapVisibility, Handle, now);

    /// <summary>
    /// Replaces name, visibility and handle together (null/blank handle = none). All rules are checked against the
    /// resulting state before anything changes: Public requires a handle; the handle must be valid and not reserved.
    /// </summary>
    public void UpdateProfile(string name, MapVisibility mapVisibility, string? handle, DateTimeOffset now)
    {
        if (!Enum.IsDefined(mapVisibility))
        {
            throw ValidationException.ForField("user.invalid_map_visibility", "mapVisibility", "Visibilidade inválida.");
        }

        var normalizedHandle = NormalizeHandle(handle);
        ValidateHandle(normalizedHandle);
        if (mapVisibility == MapVisibility.Public && normalizedHandle is null)
        {
            throw HandleRequired();
        }

        var validName = ValidateName(name);

        Name = validName;
        NormalizedName = TextNormalizer.NormalizeForComparison(validName);
        MapVisibility = mapVisibility;
        Handle = normalizedHandle;
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

    public static ValidationException HandleRequired() =>
        ValidationException.ForField("profile.handle_required", "handle", "Perfil público exige um apelido.");

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
