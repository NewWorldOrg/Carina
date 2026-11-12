using Carina.Domain.Notifications;

namespace Carina.Api.Requests;

public sealed record CreateNotificationDestinationRequest
{
    public string? Name { get; init; }

    public string? Url { get; init; }

    public override string ToString() => "a request to register a destination";
}

public sealed record ReviseNotificationDestinationRequest
{
    public string? Name { get; init; }

    public string? Url { get; init; }

    public bool? Enabled { get; init; }

    public override string ToString() => "a request to change a destination";
}

public sealed record PushKeysRequest
{
    public string? P256dh { get; init; }

    public string? Auth { get; init; }

    public override string ToString() => "the keys of a browser";
}

public sealed record CreatePushSubscriptionRequest
{
    public string? Name { get; init; }

    public string? Endpoint { get; init; }

    public PushKeysRequest? Keys { get; init; }

    public override string ToString() => "a request to register a browser";
}

public sealed record ReplacePushSubscriptionRequest
{
    public string? Endpoint { get; init; }

    public PushKeysRequest? Keys { get; init; }

    public override string ToString() => "a request to subscribe a browser again";
}

public sealed record CreateNotificationRuleRequest
{
    public string? Name { get; init; }

    public bool? Enabled { get; init; }

    public IReadOnlyList<NoticeKind>? Events { get; init; }

    public IReadOnlyList<Guid>? Destinations { get; init; }
}

public sealed record ReviseNotificationRuleRequest
{
    public string? Name { get; init; }

    public bool? Enabled { get; init; }

    public IReadOnlyList<NoticeKind>? Events { get; init; }

    public IReadOnlyList<Guid>? Destinations { get; init; }
}

public sealed record CreateNotificationWatchRequest
{
    public string? Programme { get; init; }

    public DateTimeOffset? ProgrammeStartsAt { get; init; }

    public int? LeadMinutes { get; init; }

    public IReadOnlyList<Guid>? Destinations { get; init; }
}

public sealed record PutNotificationSettingsRequest
{
    public int? NotifyLeadMinutes { get; init; }
}
