namespace Carina.Domain.Reservations;

public enum DivergedField
{
    Name = 1,

    StartAt = 2,

    EndAt = 3,

    Service = 4,
}

public sealed record EpgDivergence(DivergedField Field, string? Before, string? After, DateTime DetectedAt);
