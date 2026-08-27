using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Storage;
using Carina.Api.Services;
using Carina.Domain.Recordings;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Storage;

[ApiController]
[Route("api/storage")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetStorageAction(StorageService storage) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<StorageResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<StorageResponder>>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<BaseResponder<StorageResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<IReadOnlyList<StorageRootStanding>, StorageFailure> read =
            await storage.ReadAsync(cancellationToken);

        return read.IsSuccess
            ? Ok(BaseResponder<StorageResponder>.Success(StorageResponder.Of(read.Data!)))
            : StatusCode(
                read.ErrorType is StorageFailure.DriverUnreachable
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status502BadGateway,
                BaseResponder<StorageResponder>.Error(read.ErrorMessage!));
    }
}
