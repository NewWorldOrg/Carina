using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;

namespace Carina.Api.Tests.Feature.Playback;

internal sealed class VanishingPlaybackFiles(long bytes, PlaybackFileAbsence then = PlaybackFileAbsence.Gone) : IPlaybackFileStore
{
    public PlaybackFileSearch Find(OutputRoot root, RecordingFileName fileName)
        => PlaybackFileSearch.Of(new PlaybackFile(root, fileName, bytes));

    public PlaybackFileOpening OpenRead(PlaybackFile file) => PlaybackFileOpening.Missing(then);

    public StreamSource? SourceOf(PlaybackFile file) => null;
}
