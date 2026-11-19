namespace Carina.Domain.Streaming;

public interface ILiveEncoderSelector
{
    Task<LiveEncoderChoice> ChooseAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<VideoCodec>> FormatsAsync(CancellationToken cancellationToken);
}
