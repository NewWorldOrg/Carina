using Carina.Domain.Notifications;

namespace Carina.Api.Requests;

public sealed record CreateNotificationDestinationRequest
{
    public string? Name { get; init; }

    public string? Url { get; init; }
}

public sealed record ReviseNotificationDestinationRequest
{
    public string? Name { get; init; }

    public string? Url { get; init; }

    public bool? Enabled { get; init; }
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
