using Carina.Contracts;
using Carina.Domain.Events;

namespace Carina.TestSupport;

public sealed class SilentEvents : IAppEventPublisher
{
    public List<AppEventName> Signalled { get; } = [];

    public void Signal(AppEventName name) => Signalled.Add(name);
}
