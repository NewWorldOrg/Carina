namespace Carina.Domain.Driver;

public interface IDriverSignals
{
    IDisposable Subscribe(Action<string> listener);
}
