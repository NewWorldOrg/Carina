using Carina.Domain.Events;

using Puppis.Contracts;

namespace Carina.TestSupport;

public sealed class SilentEvents : IAppEventPublisher
{
    public List<AppEventName> Signalled { get; } = [];

    public void Signal(AppEventName name) => Signalled.Add(name);
}
