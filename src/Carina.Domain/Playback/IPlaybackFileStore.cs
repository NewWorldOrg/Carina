using Carina.Domain.Recordings;
using Carina.Domain.Streaming;

namespace Carina.Domain.Playback;

public interface IPlaybackFileStore
{
    PlaybackFile? Find(OutputRoot root, RecordingFileName fileName);

    Stream? OpenRead(PlaybackFile file);

    StreamSource? SourceOf(PlaybackFile file);
}
