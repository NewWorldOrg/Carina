using Carina.Domain.Recordings;

namespace Carina.Domain.Playback;

public interface IPlaybackFileStore
{
    PlaybackFile? Find(OutputRoot root, RecordingFileName fileName);

    Stream? OpenRead(PlaybackFile file);
}
