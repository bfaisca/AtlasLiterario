using AtlasLiterario.Application.Maps;
using AtlasLiterario.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasLiterario.API.Controllers;

[Route("api/maps")]
[Tags("Maps")]
public sealed class MapsController(IMapService mapService) : ApiControllerBase
{
    /// <summary>Authors map: citizenships of the authors of the user's Read books.</summary>
    [HttpGet("authors")]
    [ProducesResponseType<MapDataDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MapDataDto>> GetAuthorsMap(CancellationToken ct) =>
        Ok(await mapService.GetMapAsync(MapType.Authors, ct));

    /// <summary>Origins map: literary origin countries of the user's Read books.</summary>
    [HttpGet("origins")]
    [ProducesResponseType<MapDataDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MapDataDto>> GetOriginsMap(CancellationToken ct) =>
        Ok(await mapService.GetMapAsync(MapType.Origins, ct));

    /// <summary>
    /// Side panel data for one country. A valid country without books returns 200 with bookCount 0 and an empty list.
    /// </summary>
    /// <param name="type">authors | origins</param>
    /// <param name="iso">ISO 3166-1 alpha-3 (case-insensitive).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{type:regex(^(authors|origins)$)}/countries/{iso}")]
    [ProducesResponseType<CountryDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<CountryDetailDto>> GetCountryDetail(string type, string iso, CancellationToken ct) =>
        Ok(await mapService.GetCountryDetailAsync(
            string.Equals(type, "authors", StringComparison.OrdinalIgnoreCase) ? MapType.Authors : MapType.Origins, iso, ct));

    [HttpPost("share")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status501NotImplemented, "application/problem+json")]
    [ProducesResponseType<MapShareDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public ActionResult<MapShareDto> CreateShare(CreateMapShareRequest request) => NotImplementedYet("Etapa 6");

    [HttpGet("share")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status501NotImplemented, "application/problem+json")]
    [ProducesResponseType<IReadOnlyList<MapShareDto>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<MapShareDto>> ListShares() => NotImplementedYet("Etapa 6");

    [HttpDelete("share/{token}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status501NotImplemented, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult RevokeShare(string token) => NotImplementedYet("Etapa 6");
}

[Route("api/public/maps")]
[Tags("Maps")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status501NotImplemented, "application/problem+json")]
public sealed class PublicMapsController : ApiControllerBase
{
    /// <summary>Public aggregated map (no titles, reviews or dates).</summary>
    [AllowAnonymous]
    [HttpGet("{token}")]
    [ProducesResponseType<PublicMapDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public ActionResult<PublicMapDto> Get(string token) => NotImplementedYet("Etapa 6");
}
