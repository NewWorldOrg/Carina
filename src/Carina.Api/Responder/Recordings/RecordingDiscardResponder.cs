using Carina.Api.Services;

namespace Carina.Api.Responder.Recordings;

public sealed record RecordingDiscardResponder(string RecordingId, int FilesRemoved)
{
    public static RecordingDiscardResponder Of(RecordingDiscarded discarded)
    {
        ArgumentNullException.ThrowIfNull(discarded);

        return new RecordingDiscardResponder(discarded.Id.Wire, discarded.FilesRemoved);
    }
}

public sealed record RecordingDiscardRefusedResponder(string RecordingId, RecordingFailure Refusal)
{
    public static RecordingDiscardRefusedResponder Of(string recordingId, RecordingFailure refusal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingId);

        return new RecordingDiscardRefusedResponder(recordingId, refusal);
    }
}
