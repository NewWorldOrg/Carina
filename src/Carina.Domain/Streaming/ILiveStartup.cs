namespace Carina.Domain.Streaming;

public interface ILiveStartup
{
    LiveStartup? Current { get; }
}
