using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Quality;
using Carina.Api.Services;
using Carina.Domain.Quality;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Quality;

[ApiController]
[Route("api/quality/supply-health")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetQualitySupplyHealthAction(QualityIncidentService incidents) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<QualitySupplyHealthResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<QualitySupplyHealthResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<SupplyStanding> read = await incidents.SupplyHealthAsync(cancellationToken);

        return read.IsSuccess
            ? Ok(BaseResponder<QualitySupplyHealthResponder>.Success(
                QualitySupplyHealthResponder.Of(read.Data!)))
            : StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                BaseResponder<QualitySupplyHealthResponder>.Error(read.ErrorMessage!));
    }
}
