using AtlasLiterario.Application.Statistics;
using Microsoft.AspNetCore.Mvc;

namespace AtlasLiterario.API.Controllers;

[Route("api/statistics")]
[Tags("Statistics")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status501NotImplemented, "application/problem+json")]
public sealed class StatisticsController : ApiControllerBase
{
    private const string Stage = "Etapa 5";

    [HttpGet("dashboard")]
    [ProducesResponseType<DashboardDto>(StatusCodes.Status200OK)]
    public ActionResult<DashboardDto> GetDashboard() => NotImplementedYet(Stage);

    [HttpGet("passports")]
    [ProducesResponseType<PassportsDto>(StatusCodes.Status200OK)]
    public ActionResult<PassportsDto> GetPassports() => NotImplementedYet(Stage);
}
