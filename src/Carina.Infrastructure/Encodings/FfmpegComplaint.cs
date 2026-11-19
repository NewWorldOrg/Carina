using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Encodings;

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
