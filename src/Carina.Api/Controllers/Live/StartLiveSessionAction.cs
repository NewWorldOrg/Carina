using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Live;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Live;
using Carina.Api.Services;
using Carina.Domain.Auth;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Live;

[ApiController]
[Route("api/live/sessions")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class StartLiveSessionAction(LiveService live) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<LiveSessionAheadResponder>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<BaseResponder<LiveSessionAheadResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<LiveSessionAheadResponder>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<BaseResponder<LiveSessionAheadResponder>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke([FromBody] LiveSessionRequest request, CancellationToken cancellationToken)
    {
        if (LiveSessionAsking.Read(request) is not { } asked)
        {
            return BadRequest(BaseResponder<LiveSessionAheadResponder>.Error(LiveSessionAsking.WhatASessionIsAskedWith));
        }

        if (SessionClaims.SubjectOf(User) is not { } viewer)
        {
            return Unauthorized(BaseResponder<LiveSessionAheadResponder>.Error("This request carries no session."));
        }

        ServiceResult<LiveSessionAhead, LiveAheadRefusal> started =
            await live.StartAheadAsync(asked, viewer, cancellationToken);

        return started.IsSuccess
            ? Accepted(BaseResponder<LiveSessionAheadResponder>.Success(LiveSessionAheadResponder.Of(started.Data!)))
            : StatusCode(
                Of(started.ErrorType),
                BaseResponder<LiveSessionAheadResponder>.Error(started.ErrorMessage!));
    }

    private static int Of(LiveAheadRefusal refusal) => refusal switch
    {
        LiveAheadRefusal.NoSuchChannel => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status400BadRequest,
    };
}
