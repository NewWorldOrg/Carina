using Carina.Api.Services;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Api.Responder.Encoding;

/// <summary>
/// How the queue runs when nobody asked. <c>subject</c> is what the auto-run takes, and it is
/// stated rather than offered: a recording that failed has nothing to encode, one cut short has a
/// file like any other, and nothing narrows the rest (BR-ED2-004). <c>stored</c> says whether
/// somebody settled these or whether they are what the machine was deployed with.
/// <c>whereArtefactsGo</c> says whether the destinations and profiles defined settle where an
/// artefact goes when nobody asked, and which of them is missing when they do not; the auto-run
/// queues nothing until it is settled.
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
