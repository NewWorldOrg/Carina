using Carina.Contracts;
using Carina.Domain.Events;

namespace Carina.Conventions.Tests.Fixtures.Events;

internal sealed class CompliantPublisher : IAppEventPublisher
{
    public void Signal(AppEventName name)
    {
    }

    public void SignalLater(AppEventName name, CancellationToken cancellationToken)
    {
    }
}

internal sealed class PayloadPublisher : IAppEventPublisher
{
    public void Signal(AppEventName name)
    {
    }

    public void Signal(AppEventName name, string changed)
    {
    }
}

internal sealed class LoosePublisher : IAppEventPublisher
{
    public void Signal(AppEventName name)
    {
    }

    public void Signal(string name)
    {
    }
}
