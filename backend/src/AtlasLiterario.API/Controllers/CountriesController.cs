using AtlasLiterario.Application.Countries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasLiterario.API.Controllers;

[Route("api/countries")]
[Tags("Countries")]
public sealed class CountriesController(ICountryService countryService) : ApiControllerBase
{
    /// <summary>Full list of countries (ISO 3166-1). Public and cacheable.</summary>
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    [ProducesResponseType<IReadOnlyList<CountryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CountryDto>>> GetAll(CancellationToken ct) =>
        Ok(await countryService.GetAllAsync(ct));
}
