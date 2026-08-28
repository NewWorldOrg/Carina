using Carina.Domain.Programmes;

namespace Carina.Domain.Reservations;

public sealed record RuleApplicationSettings
{
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromMinutes(10);

    public TimeSpan Grace { get; init; } = DefaultGrace;

    public int Rows { get; init; } = BulkCursor.DefaultRows;
}
