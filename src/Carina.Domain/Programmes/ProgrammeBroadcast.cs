namespace Carina.Domain.Programmes;

public sealed record ProgrammeBroadcast(
    ProgrammeId Id,
    int TransportStreamId,
    DateTime StartsAt,
    DateTime? EndsAt,
    string Name,
    string Summary,
    bool IsShadow);
