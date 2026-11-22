using AtlasLiterario.Application.Statistics;
using Microsoft.AspNetCore.Mvc;

namespace AtlasLiterario.API.Controllers;

[Route("api/statistics")]
[Tags("Statistics")]
public sealed class StatisticsController(IStatisticsService statisticsService) : ApiControllerBase
{
    /// <summary>Dashboard of the logged-in user: totals, recent reads, top countries and passport progress (Read books only).</summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("dashboard")]
    [ProducesResponseType<DashboardDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardDto>> GetDashboard(CancellationToken ct) =>
        Ok(await statisticsService.GetDashboardAsync(ct));

    /// <summary>Literary Passports: per-map stats, continents, monthly timeline and progress (Read books only).</summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("passports")]
    [ProducesResponseType<PassportsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PassportsDto>> GetPassports(CancellationToken ct) =>
        Ok(await statisticsService.GetPassportsAsync(ct));
}
