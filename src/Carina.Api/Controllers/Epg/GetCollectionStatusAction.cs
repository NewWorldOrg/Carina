using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Epg;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Epg;

[ApiController]
[Route("api/epg/collection-status")]
public sealed class GetCollectionStatusAction(CollectionStatusService status) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<CollectionStatusResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<CollectionStatus> read = await status.ReadAsync(cancellationToken);

        return Ok(BaseResponder<CollectionStatusResponder>.Success(
            CollectionStatusResponder.Of(read.Data!)));
    }
}
