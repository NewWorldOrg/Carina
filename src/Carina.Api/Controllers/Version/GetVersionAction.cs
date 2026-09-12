using Carina.Api.Authentication;
using Carina.Api.Responder;
using Carina.Api.Responder.Version;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Version;

[ApiController]
[Route("api/version")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetVersionAction(VersionService version) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<VersionResponder>>(StatusCodes.Status200OK)]
    public IActionResult Invoke()
        => Ok(BaseResponder<VersionResponder>.Success(VersionResponder.Of(version.Read().Data!)));
}
