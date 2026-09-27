using Carina.Api.Services;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Api.Responder.Encoding;

/// <summary>
/// How the queue runs when nobody asked. <c>subject</c> states what the auto-run takes.
/// <c>stored</c> says whether somebody settled these or they are the deployed settings.
/// <c>whereArtefactsGo</c> says whether the destinations and profiles defined settle where an
/// artefact goes when nobody asked, and which of them is missing when they do not.
/// </summary>
public sealed record EncodeAutoRunResponder(
    bool Automatically,
    int MostCores,
    int CoresThisMachineHas,
    IReadOnlyList<RecordingOutcome> Subject,
    bool Stored,
    DateTime? UpdatedAt,
    EncodeUnaskedStanding WhereArtefactsGo)
{
    public static EncodeAutoRunResponder Of(EncodeAutoRunReading reading, int coresThisMachineHas)
    {
        ArgumentNullException.ThrowIfNull(reading);

        EncodeAutoRunStanding standing = reading.Standing;

        return new EncodeAutoRunResponder(
            standing.Automatically,
            standing.MostCores,
            coresThisMachineHas,
            EncodeAutoRun.Subject,
            standing.Stored,
            standing.UpdatedAt,
            reading.WhereArtefactsGo);
    }
}
