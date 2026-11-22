using AtlasLiterario.Application.Common;
using AtlasLiterario.Application.Maps;
using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Application.Profiles;

public interface IProfileService
{
    /// <summary>Public profiles by handle prefix or name-word prefix (accent/case-insensitive); excludes the caller.</summary>
    Task<IReadOnlyList<ProfileSummaryDto>> SearchAsync(string? q, int? limit, CancellationToken ct);

    /// <summary><c>profile.not_found</c> when the handle does not exist or is not Public (except for the caller's own profile).</summary>
    Task<ProfileDto> GetAsync(string handle, CancellationToken ct);

    /// <summary>Idempotent. <c>follow.self</c> (400); target must be Public (otherwise 404).</summary>
    Task FollowAsync(string handle, CancellationToken ct);

    /// <summary>Idempotent and always allowed, even if the profile became private or does not exist.</summary>
    Task UnfollowAsync(string handle, CancellationToken ct);

    Task<PagedResult<ProfileSummaryDto>> GetFollowersAsync(string handle, int? page, int? pageSize, CancellationToken ct);

    Task<PagedResult<ProfileSummaryDto>> GetFollowingAsync(string handle, int? page, int? pageSize, CancellationToken ct);

    Task<PagedResult<ProfileSummaryDto>> GetMyFollowersAsync(int? page, int? pageSize, CancellationToken ct);

    Task<PagedResult<ProfileSummaryDto>> GetMyFollowingAsync(int? page, int? pageSize, CancellationToken ct);

    /// <summary>Owner's map, Read books only.</summary>
    Task<MapDataDto> GetMapAsync(string handle, MapType mapType, CancellationToken ct);

    Task<ProfileCountryDetailDto> GetCountryDetailAsync(string handle, MapType mapType, string iso, CancellationToken ct);
}
