using AtlasLiterario.API.Configuration;
using AtlasLiterario.Application.Common;
using AtlasLiterario.Application.Maps;
using AtlasLiterario.Application.Profiles;
using AtlasLiterario.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AtlasLiterario.API.Controllers;

/// <summary>Public profiles, follows and profile maps. Authentication is always required.</summary>
[Route("api/profiles")]
[Tags("Profiles")]
public sealed class ProfilesController(IProfileService profiles) : ApiControllerBase
{
    /// <summary>Searches Public profiles by handle prefix or name-word prefix (accent/case-insensitive). Rate limited per user.</summary>
    /// <param name="q">At least 2 characters after trimming.</param>
    /// <param name="limit">1–20 (default 10).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [EnableRateLimiting(ApiServiceCollectionExtensions.ProfileSearchPolicy)]
    [ProducesResponseType<IReadOnlyList<ProfileSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<ProfileSummaryDto>>> Search(
        [FromQuery] string? q, [FromQuery] int? limit, CancellationToken ct) =>
        Ok(await profiles.SearchAsync(q, limit, ct));

    [HttpGet("{handle}")]
    [ProducesResponseType<ProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ProfileDto>> Get(string handle, CancellationToken ct) =>
        Ok(await profiles.GetAsync(handle, ct));

    /// <summary>Follows the profile (idempotent). Only Public profiles can be followed.</summary>
    [HttpPost("{handle}/follow")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Follow(string handle, CancellationToken ct)
    {
        await profiles.FollowAsync(handle, ct);
        return NoContent();
    }

    /// <summary>Unfollows (idempotent; allowed even if the profile became private).</summary>
    [HttpDelete("{handle}/follow")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unfollow(string handle, CancellationToken ct)
    {
        await profiles.UnfollowAsync(handle, ct);
        return NoContent();
    }

    [HttpGet("{handle}/followers")]
    [ProducesResponseType<PagedResult<ProfileSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<PagedResult<ProfileSummaryDto>>> Followers(
        string handle, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await profiles.GetFollowersAsync(handle, page, pageSize, ct));

    [HttpGet("{handle}/following")]
    [ProducesResponseType<PagedResult<ProfileSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<PagedResult<ProfileSummaryDto>>> Following(
        string handle, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await profiles.GetFollowingAsync(handle, page, pageSize, ct));

    /// <summary>The profile's map (Read books only; no status filter).</summary>
    /// <param name="handle">Profile handle.</param>
    /// <param name="type">authors | origins</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{handle}/maps/{type:regex(^(authors|origins)$)}")]
    [ProducesResponseType<MapDataDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<MapDataDto>> GetMap(string handle, string type, CancellationToken ct) =>
        Ok(await profiles.GetMapAsync(handle, ToMapType(type), ct));

    /// <summary>Country panel of the profile's map, with the owner's Read books, authors and reviews.</summary>
    [HttpGet("{handle}/maps/{type:regex(^(authors|origins)$)}/countries/{iso}")]
    [ProducesResponseType<ProfileCountryDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ProfileCountryDetailDto>> GetCountry(
        string handle, string type, string iso, CancellationToken ct) =>
        Ok(await profiles.GetCountryDetailAsync(handle, ToMapType(type), iso, ct));

    private static MapType ToMapType(string type) =>
        string.Equals(type, "authors", StringComparison.OrdinalIgnoreCase) ? MapType.Authors : MapType.Origins;
}

/// <summary>The caller's own follower/following lists (available even when the own profile is not Public).</summary>
[Route("api/me")]
[Tags("Profiles")]
public sealed class MeSocialController(IProfileService profiles) : ApiControllerBase
{
    [HttpGet("followers")]
    [ProducesResponseType<PagedResult<ProfileSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProfileSummaryDto>>> Followers(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await profiles.GetMyFollowersAsync(page, pageSize, ct));

    [HttpGet("following")]
    [ProducesResponseType<PagedResult<ProfileSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProfileSummaryDto>>> Following(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await profiles.GetMyFollowingAsync(page, pageSize, ct));
}
