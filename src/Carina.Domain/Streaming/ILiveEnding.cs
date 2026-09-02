namespace Carina.Domain.Streaming;

public interface ILiveEnding
{
    LiveSupplyEnding? Current { get; }
}
