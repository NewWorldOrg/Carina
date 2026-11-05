using Puppis.Contracts;

namespace Carina.Domain.Events;

public interface IAppEventPublisher
{
    void Signal(AppEventName name);
}
