using Carina.Domain.Notifications;

namespace Carina.Api.Common;

public static class NotificationIdText
{
    public const string Description = "A destination or a set of rules is named by a UUID, and never by one that is all zeroes.";

    public static NotificationDestinationId? Destination(Guid? id)
        => id is { } some && some != Guid.Empty ? new NotificationDestinationId(some) : null;

    public static NotificationRuleId? Rule(Guid? id) => id is { } some && some != Guid.Empty ? new NotificationRuleId(some) : null;
}
