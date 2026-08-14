using Carina.Contracts;
using Carina.Domain.Events;

namespace Carina.Infrastructure.Events;

public sealed class NoopAppEventPublisher : IAppEventPublisher
{
    public void Signal(AppEventName name)
    {
    }
}
