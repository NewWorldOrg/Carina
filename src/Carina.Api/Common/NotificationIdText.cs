using Carina.Domain.Notifications;

namespace Carina.Api.Common;

public static class NotificationIdText
{
    public const string Description = "A destination, a set of rules or a programme asked to be told of is named by a UUID, and never by one that is all zeroes.";

    public static NotificationDestinationId? Destination(Guid? id)
        => id is { } some && some != Guid.Empty ? new NotificationDestinationId(some) : null;

    public static NotificationRuleId? Rule(Guid? id) => id is { } some && some != Guid.Empty ? new NotificationRuleId(some) : null;

    public static ProgrammeWatchId? Watch(Guid? id) => id is { } some && some != Guid.Empty ? new ProgrammeWatchId(some) : null;
}
