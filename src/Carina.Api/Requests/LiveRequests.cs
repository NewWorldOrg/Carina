namespace Carina.Api.Requests;

public sealed record LiveTicketRequest
{
    public int? NetworkId { get; init; }

    public int? ServiceId { get; init; }
}
