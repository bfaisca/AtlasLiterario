namespace AtlasLiterario.Domain.Enums;

/// <summary>
/// Account role. Promotion is only done by a documented SQL script (no public endpoint, no e-mail based rule:
/// e-mail addresses are not verified). Permission checks read the role from the database, not from the token.
/// </summary>
public enum UserRole : short
{
    User = 0,
    Moderator = 1,
}
