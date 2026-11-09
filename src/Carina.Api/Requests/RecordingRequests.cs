namespace Carina.Api.Requests;

public sealed record StopRecordingRequest
{
    public string? Reason { get; init; }
}

public sealed record RemakeThumbnailsRequest
{
    public IReadOnlyList<string>? Ids { get; init; }
}
