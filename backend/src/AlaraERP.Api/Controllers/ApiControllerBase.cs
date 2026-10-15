using Microsoft.AspNetCore.Mvc;

namespace AlaraERP.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>A problem response carrying a stable machine-readable <c>code</c>.</summary>
    protected ObjectResult ProblemWithCode(int status, string code, string title, string? detail = null) =>
        new(new ProblemDetails { Status = status, Title = title, Detail = detail, Extensions = { ["code"] = code } })
        {
            StatusCode = status,
        };
}
