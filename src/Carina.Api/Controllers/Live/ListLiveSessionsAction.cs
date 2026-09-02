using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Live;
using Carina.Api.Services;
using Carina.Domain.Streaming;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Live;

[ApiController]
[Route("api/live/sessions")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListLiveSessionsAction(LiveService live) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<IReadOnlyList<LiveSessionResponder>>>(StatusCodes.Status200OK)]
    public IActionResult Invoke()
    {
        ServiceResult<IReadOnlyList<LiveSessionView>> running = live.ListSessions();

        return Ok(BaseResponder<IReadOnlyList<LiveSessionResponder>>.Success(
            [.. running.Data!.Select(LiveSessionResponder.Of)]));
    }
}
