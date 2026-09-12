using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Live;
using Carina.Api.Services;
using Carina.Domain.Streaming;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Live;

[ApiController]
[Route("api/live/departures")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListLiveDeparturesAction(LiveService live) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<LiveDepartureTallyResponder>>(StatusCodes.Status200OK)]
    public IActionResult Invoke()
    {
        ServiceResult<LiveDepartureTally> tally = live.ReadDepartures();

        return Ok(BaseResponder<LiveDepartureTallyResponder>.Success(
            LiveDepartureTallyResponder.Of(tally.Data!)));
    }
}
