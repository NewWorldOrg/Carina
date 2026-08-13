using Carina.Contracts;

namespace Carina.Domain.Events;

public interface IAppEventPublisher
{
    void Signal(AppEventName name);
}
