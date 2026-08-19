using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Services;
using Carina.Domain.Channels;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Epg;

public sealed record ArchiveForgottenResponder(int Forgotten);

[ApiController]
[Route("api/epg/archive/forget-service")]
[EndpointEffect(EndpointEffect.Destructive)]
public sealed class ForgetArchivedServiceAction(ArchiveService archive) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<ArchiveForgottenResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<ArchiveForgottenResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromBody] ForgetArchivedServiceRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.MeansIt is not true)
        {
            return BadRequest(BaseResponder<ArchiveForgottenResponder>.Error(
                $"Letting go of a service's history needs confirm to say "
                + $"'{ForgetArchivedServiceRequest.TheWordThatMeansIt}'."));
        }

        (NetworkId? networkId, ServiceId? serviceId) = Named(request);

        if (networkId is null || serviceId is null)
        {
            return BadRequest(BaseResponder<ArchiveForgottenResponder>.Error(
                "A service is named by its network and service id."));
        }

        ServiceResult<ArchiveForgotten> forgotten = await archive.ForgetServiceAsync(
            networkId,
            serviceId,
            cancellationToken);

        return Ok(BaseResponder<ArchiveForgottenResponder>.Success(
            new ArchiveForgottenResponder(forgotten.Data!.Forgotten)));
    }

    private static (NetworkId? Network, ServiceId? Service) Named(ForgetArchivedServiceRequest request)
    {
        try
        {
            return request.NetworkId is { } network && request.ServiceId is { } service
                ? (new NetworkId(network), new ServiceId(service))
                : (null, null);
        }
        catch (ArgumentOutOfRangeException)
        {
            return (null, null);
        }
    }
}
