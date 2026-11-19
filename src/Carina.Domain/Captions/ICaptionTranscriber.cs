using Carina.Domain.Channels;

namespace Carina.Domain.Captions;

public interface ICaptionTranscriber
{
    Task<CaptionTranscription> TranscribeAsync(string source, ServiceId service, CancellationToken cancellationToken);
}
