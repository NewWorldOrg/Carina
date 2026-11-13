using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;

namespace Carina.Api.Tests.Feature.Playback;

internal sealed class GatedPlaybackFiles(byte[] bytes) : IPlaybackFileStore
{
    public GatedStream Handed { get; } = new(bytes);

    public PlaybackFileSearch Find(OutputRoot root, RecordingFileName fileName)
        => PlaybackFileSearch.Of(new PlaybackFile(root, fileName, bytes.Length));

    public PlaybackFileOpening OpenRead(PlaybackFile file) => PlaybackFileOpening.Of(Handed);

    public StreamSource? SourceOf(PlaybackFile file) => null;
}
