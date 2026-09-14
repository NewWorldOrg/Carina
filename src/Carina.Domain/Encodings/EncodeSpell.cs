using Carina.Domain.Base;

namespace Carina.Domain.Encodings;

/// <summary>
/// How long one job that finished took, and when it finished. Both come off the ledger's own two
/// marks, so nothing here is measured against a clock this process happens to be holding.
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
/// What the jobs that finished say about how long an encode takes on this machine. An average of
/// one job is not an average, so nothing is averaged until <see cref="FewestToAverage"/> of them
/// have finished: until then the answer says how many there are and leaves the average unsaid,
/// rather than answering zero as though an encode here were instant.
/// <para>
/// The window is the two ends of what was counted, so a reader can tell an average made this week
/// from one made in the spring. Nothing is split by profile: the spread that matters here is which
/// encoder ran, and a machine that swerved to the processor is the same machine an hour later.
/// </para>
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
