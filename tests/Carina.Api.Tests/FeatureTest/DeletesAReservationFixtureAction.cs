using Carina.Api.Authentication;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Tests.FeatureTest;

[ApiController]
[Route("api/reservations/{id}/fixture-only")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class DeletesAReservationFixtureAction : ControllerBase
{
    [HttpDeleteAttribute]
    public IActionResult Invoke(string id) => NoContent();
}
