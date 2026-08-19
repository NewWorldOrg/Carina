using Carina.Api.Authentication;
using Carina.Api.Responder.Health;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Health;

[ApiController]
[Route("api/health")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetHealthAction(HealthService health) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<HealthResponder>(StatusCodes.Status200OK)]
    public IActionResult Invoke() => Ok(HealthResponder.Of(health.Read().Data!));
}
