using Carina.Api.Authentication;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Tests.FeatureTest;

[ApiController]
[Route("api/recordings/{id}/fixture-only")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class DeletesARecordingFileFixtureAction : ControllerBase
{
    [HttpDeleteAttribute]
    public IActionResult Invoke(string id) => NoContent();
}
