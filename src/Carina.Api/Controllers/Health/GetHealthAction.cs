using Carina.Api.Responder.Health;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Health;

[ApiController]
[Route("api/health")]
[AllowAnonymous]
public sealed class GetHealthAction : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<HealthResponder>(StatusCodes.Status200OK)]
    public IActionResult Invoke() => Ok(new HealthResponder("ok"));
}
