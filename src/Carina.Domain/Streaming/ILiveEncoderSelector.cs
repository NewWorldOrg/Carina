namespace Carina.Domain.Streaming;

public interface ILiveEncoderSelector
{
    Task<LiveEncoderChoice> ChooseAsync(CancellationToken cancellationToken);
}
