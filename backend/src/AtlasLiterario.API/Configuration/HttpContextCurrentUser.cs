using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Domain.Exceptions;

namespace AtlasLiterario.API.Configuration;

/// <summary>Current user from the JWT <c>sub</c> claim (inbound claim mapping is disabled).</summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public const string SubjectClaim = "sub";

    public Guid? UserId
    {
        get
        {
            var subject = httpContextAccessor.HttpContext?.User.FindFirst(SubjectClaim)?.Value;
            return Guid.TryParse(subject, out var id) ? id : null;
        }
    }

    public bool IsAuthenticated => UserId.HasValue;

    public Guid GetRequiredUserId() =>
        UserId ?? throw new UnauthorizedException("auth.unauthorized", "Autenticação necessária.");
}
