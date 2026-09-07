namespace Carina.Domain.Migration;

public sealed record SourceReservation
{
    public SourceReservation(long id, string programmeName, bool fromARule)
    {
        ArgumentNullException.ThrowIfNull(programmeName);

        Id = SourceRow.Of(id, nameof(id));
        ProgrammeName = programmeName;
        FromARule = fromARule;
    }

    public long Id { get; }

    public string ProgrammeName { get; }

    public bool FromARule { get; }
}
