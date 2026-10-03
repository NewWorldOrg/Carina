using Carina.Domain.Channels;

namespace Carina.Domain.Captions;

public interface ICaptionTranscriber
{
    /// <summary>
    /// Draws the captions of one service out of a recording's file the way live viewing draws them,
    /// without decoding the picture.
    /// </summary>
    Task<CaptionTranscription> TranscribeAsync(string source, ServiceId service, CancellationToken cancellationToken);
}
