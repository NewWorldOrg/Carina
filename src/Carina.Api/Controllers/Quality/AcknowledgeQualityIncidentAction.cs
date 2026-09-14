using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Quality;
using Carina.Api.Services;
using Carina.Domain.Auth;
using Carina.Domain.Quality;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Quality;

[ApiController]
[Route("api/quality/incidents/{id}/acknowledge")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class AcknowledgeQualityIncidentAction(QualityIncidentService incidents) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<QualityIncidentResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<QualityIncidentResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<QualityIncidentResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke(string id, CancellationToken cancellationToken)
    {
        if (SessionClaims.SubjectOf(User) is not { } who)
        {
            return Unauthorized();
        }

        ServiceResult<QualityIncident, QualityIncidentFailure> acknowledged =
            await incidents.AcknowledgeAsync(id, who, cancellationToken);

        if (acknowledged.IsSuccess)
        {
            return Ok(BaseResponder<QualityIncidentResponder>.Success(
                QualityIncidentResponder.Of(acknowledged.Data!)));
        }

        BaseResponder<QualityIncidentResponder> refused =
            BaseResponder<QualityIncidentResponder>.Error(acknowledged.ErrorMessage!);

        return acknowledged.ErrorType is QualityIncidentFailure.NoSuchIncident
            ? NotFound(refused)
            : Conflict(refused);
    }
}
