using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Quality;
using Carina.Api.Services;
using Carina.Domain.Quality;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Quality;

[ApiController]
[Route("api/quality/incidents")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListQualityIncidentsAction(QualityIncidentService incidents) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<QualityIncidentListResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<IReadOnlyList<QualityIncident>> read = await incidents.ListAsync(cancellationToken);

        return Ok(BaseResponder<QualityIncidentListResponder>.Success(
            QualityIncidentListResponder.Of(read.Data!)));
    }
}
