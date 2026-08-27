namespace Carina.Api.Requests;

public sealed record CreateReservationRequest
{
    public string? Programme { get; init; }

    public DateTimeOffset? ProgrammeStartsAt { get; init; }

    public int? Priority { get; init; }

    public int? MarginBeforeSeconds { get; init; }

    public int? MarginAfterSeconds { get; init; }
}

public sealed record ReviseReservationRequest
{
    public int? Priority { get; init; }

    public int? MarginBeforeSeconds { get; init; }

    public int? MarginAfterSeconds { get; init; }
}
