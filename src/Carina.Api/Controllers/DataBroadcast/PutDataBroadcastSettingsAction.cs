using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.DataBroadcast;
using Carina.Api.Services;
using Carina.Domain.DataBroadcast;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.DataBroadcast;

/// <summary>
/// Registers where the receiver is, for data broadcast documents to read: the postal code and the prefecture are sent
/// together, and either may be null, which leaves it unregistered.
/// </summary>
[ApiController]
[Route("api/data-broadcast/settings")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class PutDataBroadcastSettingsAction(DataBroadcastSettingsService settings) : ControllerBase
{
    [HttpPut]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<DataBroadcastSettingsResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<DataBroadcastSettingsResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromBody] PutDataBroadcastSettingsRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(BaseResponder<DataBroadcastSettingsResponder>.Error(
                "the body: expected the postal code and the prefecture, each of them null for none."));
        }

        ServiceResult<DataBroadcastSettingsStanding> changed =
            await settings.ChangeAsync(request.PostalCode, request.Prefecture, cancellationToken);

        return changed.Data is { } standing
            ? Ok(BaseResponder<DataBroadcastSettingsResponder>.Success(DataBroadcastSettingsResponder.Of(standing)))
            : BadRequest(BaseResponder<DataBroadcastSettingsResponder>.Error(changed.ErrorMessage!));
    }
}
