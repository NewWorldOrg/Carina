using Carina.Api.Services;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Api.Responder.Encoding;

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
