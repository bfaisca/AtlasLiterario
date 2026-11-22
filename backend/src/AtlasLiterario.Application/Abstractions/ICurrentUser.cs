namespace AtlasLiterario.Application.Abstractions;

public interface ICurrentUser
{
    /// <summary>Authenticated user id (JWT <c>sub</c>), or null when anonymous.</summary>
    Guid? UserId { get; }

    bool IsAuthenticated { get; }

    /// <summary>Returns the user id or throws <c>UnauthorizedException</c>.</summary>
    Guid GetRequiredUserId();
}
