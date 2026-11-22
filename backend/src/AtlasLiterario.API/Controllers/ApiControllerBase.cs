using AtlasLiterario.API.ErrorHandling;
using Microsoft.AspNetCore.Mvc;

namespace AtlasLiterario.API.Controllers;

[ApiController]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>501 placeholder for contract endpoints whose implementation belongs to a later stage.</summary>
    protected ObjectResult NotImplementedYet(string stage)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status501NotImplemented,
            Title = "Not Implemented",
            Detail = $"Endpoint definido no contrato; implementação prevista para a {stage}.",
            Instance = HttpContext.Request.Path,
        };
        problem.Extensions[ProblemDetailsDefaults.CodeKey] = "feature.not_implemented";

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status501NotImplemented,
            ContentTypes = { "application/problem+json" },
        };
    }
}
