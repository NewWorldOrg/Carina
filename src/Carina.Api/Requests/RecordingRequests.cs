namespace Carina.Api.Requests;

public sealed record StopRecordingRequest
{
    public string? Reason { get; init; }
}
