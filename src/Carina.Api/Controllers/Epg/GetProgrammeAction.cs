using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Epg;
using Carina.Api.Services;
using Carina.Domain.Programmes;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Epg;

[ApiController]
[Route("api/programs/{id}")]
public sealed class GetProgrammeAction(ProgrammeGuideService guide) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<ProgrammeResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<ProgrammeResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<ProgrammeResponder>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke(string id, CancellationToken cancellationToken)
    {
        if (ProgrammeIdText.Read(id) is not { } programmeId)
        {
            return BadRequest(BaseResponder<ProgrammeResponder>.Error(
                "A programme is named by network, service and event, as in 4-1049-1."));
        }

        ServiceResult<Programme> found = await guide.FindAsync(programmeId, cancellationToken);

        return found.IsSuccess
            ? Ok(BaseResponder<ProgrammeResponder>.Success(ProgrammeResponder.Of(found.Data!)))
            : NotFound(BaseResponder<ProgrammeResponder>.Error(found.ErrorMessage!));
    }
}
