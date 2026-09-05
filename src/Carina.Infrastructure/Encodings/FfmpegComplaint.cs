using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Which of the ledger's reasons a non-zero exit is: the one the disk explains is told apart, and
/// everything else is the programme refusing. The words themselves go into the note beside the
/// classification, never in its place (BR-ED2-012).
/// </summary>
public static class FfmpegComplaint
{
    public const string OutOfRoom = "No space left on device";

    public static EncodeFailure Classified(string complained)
    {
        ArgumentNullException.ThrowIfNull(complained);

        return complained.Contains(OutOfRoom, StringComparison.OrdinalIgnoreCase)
            ? EncodeFailure.NotEnoughRoom
            : EncodeFailure.FfmpegExitedNonZero;
    }
}
