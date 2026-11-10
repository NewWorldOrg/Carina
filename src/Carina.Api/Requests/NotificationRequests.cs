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
