using AtlasLiterario.Domain.Exceptions;

namespace AtlasLiterario.Domain.Entities;

/// <summary>Asymmetric follow relation: <see cref="FollowerId"/> follows <see cref="FollowedId"/> (no acceptance, no friendship).</summary>
public sealed class UserFollow
{
    private UserFollow()
    {
    }

    public UserFollow(Guid followerId, Guid followedId, DateTimeOffset now)
    {
        if (followerId == followedId)
        {
            throw new ValidationException("follow.self", "Não é possível seguir a si mesmo.");
        }

        FollowerId = followerId;
        FollowedId = followedId;
        CreatedAt = now;
    }

    public Guid FollowerId { get; private set; }

    public Guid FollowedId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
