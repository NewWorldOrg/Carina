namespace Carina.Api.Requests;

public sealed record LiveTicketRequest
{
    public int? NetworkId { get; init; }

    public int? ServiceId { get; init; }
}

public sealed record LiveSessionRequest
{
    public int? NetworkId { get; init; }

    public int? ServiceId { get; init; }

    public string? Profile { get; init; }

    public string? Sound { get; init; }

    public string? Format { get; init; }
}
