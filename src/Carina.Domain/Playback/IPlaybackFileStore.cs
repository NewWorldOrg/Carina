using Carina.Domain.Recordings;
using Carina.Domain.Streaming;

namespace Carina.Domain.Playback;

public interface IPlaybackFileStore
{
    PlaybackFileSearch Find(OutputRoot root, RecordingFileName fileName);

    PlaybackFileOpening OpenRead(PlaybackFile file);

    StreamSource? SourceOf(PlaybackFile file);
}
