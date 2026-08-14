using Carina.Api.Responder;
using Carina.Api.Responder.Services;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Services;

[ApiController]
[Route("api/services")]
public sealed class ListServicesAction(ChannelCatalogService channelCatalogService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<IReadOnlyList<BroadcastServiceResponder>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        var result = await channelCatalogService.ListAsync(cancellationToken);

        return Ok(BaseResponder<IReadOnlyList<BroadcastServiceResponder>>.Success(
            [.. result.Data!.Select(BroadcastServiceResponder.Of)]));
    }
}
