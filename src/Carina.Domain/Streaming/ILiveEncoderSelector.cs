namespace Carina.Domain.Streaming;

public interface ILiveEncoderSelector
{
    Task<LiveEncoderChoice> ChooseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The formats a picture can be transcoded in on this machine, H.264 first and always among them.
    /// </summary>
    Task<IReadOnlyList<VideoCodec>> FormatsAsync(CancellationToken cancellationToken);
}
