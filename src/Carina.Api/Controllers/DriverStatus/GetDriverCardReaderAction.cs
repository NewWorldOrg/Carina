using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.DriverStatus;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

using Puppis.Contracts;

namespace Carina.Api.Controllers.DriverStatus;

[ApiController]
[Route("api/driver/card-reader")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetDriverCardReaderAction(DriverCardReaderService driverCardReaderService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<CardReaderResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<CardReaderResponder>>(StatusCodes.Status501NotImplemented)]
    [ProducesResponseType<BaseResponder<CardReaderResponder>>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<BaseResponder<CardReaderResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<CardReaderDto, CardReaderFailure> result = await driverCardReaderService.ReadAsync(cancellationToken);

        if (!result.IsSuccess)
        {
            return StatusCode(
                StatusOf(result.ErrorType),
                BaseResponder<CardReaderResponder>.Error(result.ErrorMessage!));
        }

        return Ok(BaseResponder<CardReaderResponder>.Success(CardReaderResponder.Of(result.Data!)));
    }

    private static int StatusOf(CardReaderFailure failure) => failure switch
    {
        CardReaderFailure.DriverUnreachable => StatusCodes.Status503ServiceUnavailable,
        CardReaderFailure.CapabilityMissing => StatusCodes.Status501NotImplemented,
        _ => StatusCodes.Status502BadGateway,
    };
}
