using Carina.Domain.Base;

namespace Carina.Domain.Encodings;

/// <summary>
/// How long one job that finished took, and when it finished, both taken from the ledger's own
/// marks.
/// </summary>
public sealed record EncodeSpell
{
    public EncodeSpell(DateTime endedAt, TimeSpan took)
    {
        if (took < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(took),
                took,
                "A job that finished took some length of time, and no job took less than none.");
        }

        EndedAt = UtcTimes.Required(endedAt, nameof(endedAt));
        Took = took;
    }

    public DateTime EndedAt { get; }

    public TimeSpan Took { get; }
}

/// <summary>
/// What the jobs that finished say about how long an encode takes on this machine. The average is
/// null until <see cref="FewestToAverage"/> of them have finished; the count and the window, the two
/// ends of what was counted, are answered either way. Nothing is split by profile.
/// </summary>
public sealed record EncodeSpells(int Counted, TimeSpan? Average, DateTime? Oldest, DateTime? Newest)
{
    public const int FewestToAverage = 3;

    public const int MostLookedAt = 20;

    public bool CanBeAveraged => Average is not null;

    public static EncodeSpells Of(IReadOnlyList<EncodeSpell> spells)
    {
        ArgumentNullException.ThrowIfNull(spells);

        if (spells.Count is 0)
        {
            return new EncodeSpells(0, null, null, null);
        }

        EncodeSpell[] counted = [.. spells.OrderBy(spell => spell.EndedAt)];

        return new EncodeSpells(
            counted.Length,
            counted.Length >= FewestToAverage
                ? TimeSpan.FromTicks(counted.Sum(spell => spell.Took.Ticks) / counted.Length)
                : null,
            counted[0].EndedAt,
            counted[^1].EndedAt);
    }
}
