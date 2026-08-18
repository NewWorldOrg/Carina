namespace Carina.Api.Requests;

public sealed record CollectNowRequest
{
    public int? NetworkId { get; init; }

    public int? TransportStreamId { get; init; }

    public int? ServiceId { get; init; }
}
