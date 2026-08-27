using Carina.Domain.Recordings;

namespace Carina.Api.Common;

public static class RecordingIdText
{
    public const string Description =
        "A recording is named by the thirty-two hexadecimal digits the ledger holds, without separators.";

    public static RecordingId? Read(string? text)
        => Guid.TryParseExact(text, "N", out Guid parsed) && parsed != Guid.Empty
            ? new RecordingId(parsed)
            : null;
}
