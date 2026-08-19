using Carina.Api.Authentication;
using Carina.Api.Responder.Health;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Health;

[ApiController]
[Route("api/health")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetHealthAction : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<HealthResponder>(StatusCodes.Status200OK)]
    public IActionResult Invoke() => Ok(new HealthResponder("ok"));
}
