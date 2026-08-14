using Carina.Broadcast.Descriptors;

namespace Carina.Broadcast.Tables;

public sealed class DescribedService
{
    internal DescribedService(
        int serviceId,
        bool carriesScheduleEvents,
        bool carriesPresentFollowingEvents,
        int runningStatus,
        bool isConditionalAccess,
        IReadOnlyList<Descriptor> descriptors)
    {
        ServiceId = serviceId;
        CarriesScheduleEvents = carriesScheduleEvents;
        CarriesPresentFollowingEvents = carriesPresentFollowingEvents;
        RunningStatus = runningStatus;
        IsConditionalAccess = isConditionalAccess;
        Descriptors = descriptors;

        if (descriptors.WithTag(DescriptorTags.Service) is { } service
            && ServiceDescription.TryRead(service, out var description))
        {
            Description = description;
        }
    }

    public int ServiceId { get; }

    public bool CarriesScheduleEvents { get; }

    public bool CarriesPresentFollowingEvents { get; }

    public int RunningStatus { get; }

    public bool IsConditionalAccess { get; }

    public ServiceDescription? Description { get; }

    public string Name => Description?.Name ?? string.Empty;

    public string ProviderName => Description?.ProviderName ?? string.Empty;

    public ServiceKind Kind => Description?.Kind ?? ServiceKind.Unknown;

    public IReadOnlyList<Descriptor> Descriptors { get; }
}
