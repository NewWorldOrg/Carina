using Carina.Domain.Notifications;

namespace Carina.Api.Responder.Notifications;

public sealed record NotificationRuleResponder(
    Guid Id,
    string Name,
    bool Enabled,
    IReadOnlyList<NoticeKind> Events,
    IReadOnlyList<Guid> Destinations,
    DateTime CreatedAt,
    DateTime ChangedAt)
{
    public static NotificationRuleResponder Of(NotificationRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new NotificationRuleResponder(
            rule.Id.Value,
            rule.Name.Value,
            rule.Enabled,
            rule.Events,
            [.. rule.Destinations.Select(destination => destination.Value)],
            rule.CreatedAt,
            rule.ChangedAt);
    }
}

public sealed record NotificationRuleListResponder(IReadOnlyList<NotificationRuleResponder> Items)
{
    public static NotificationRuleListResponder Of(IReadOnlyList<NotificationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return new NotificationRuleListResponder([.. rules.Select(NotificationRuleResponder.Of)]);
    }
}
