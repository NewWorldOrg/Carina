using Carina.Domain.Base;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

/// <summary>
/// The one row that says how the queue runs when nobody asked: whether a recording that ends is
/// queued at all, and how many of the machine's cores a run may take. Both start as the machine was
/// deployed and stay that way until somebody settles them, which is why a machine nobody has
/// touched holds no row at all (BR-ED2-004 / BR-ED2-005).
/// <para>
/// What the auto-run takes is not among them. A recording that failed has nothing to encode and one
/// cut short has a file like any other, and nothing else narrows it — no genre, no list, no third
/// answer — so <see cref="Subject"/> is a fact the surface states rather than a setting anyone
/// changes. Nor does the row say anything about giving way to someone watching: that is not a
/// preference either.
/// </para>
/// </summary>
public sealed class EncodeAutoRun
{
    public const int TheOnlyRow = 1;

    public const int FewestCores = 1;

    public const int MostCoresAnyMachineHas = 256;

    public static readonly IReadOnlyList<RecordingOutcome> Subject =
        [RecordingOutcome.Complete, RecordingOutcome.Truncated];

    private EncodeAutoRun()
    {
    }

    public int Id { get; private set; }

    public bool Automatically { get; private set; }

    public int MostCores { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static EncodeAutoRun Settled(bool automatically, int mostCores, DateTime at)
        => Rehydrate(TheOnlyRow, automatically, mostCores, at);

    public static int CoresOn(MachineSettings machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return Math.Clamp(machine.Cores, FewestCores, MostCoresAnyMachineHas);
    }

    public static EncodeAutoRun Rehydrate(int id, bool automatically, int mostCores, DateTime updatedAt)
    {
        Counted(mostCores);

        return new EncodeAutoRun
        {
            Id = id,
            Automatically = automatically,
            MostCores = mostCores,
            UpdatedAt = UtcTimes.Required(updatedAt, nameof(updatedAt)),
        };
    }

    private static void Counted(int mostCores)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mostCores, FewestCores, nameof(mostCores));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(mostCores, MostCoresAnyMachineHas, nameof(mostCores));
    }
}

/// <summary>
/// How the queue runs as it stands: the settled row where there is one, and the machine's deployed
/// settings where there is not. <see cref="Stored"/> is what tells the two apart, so a surface can
/// say whether somebody chose these or whether they are what the machine was started with.
/// <para>
/// <see cref="MostCores"/> is what a run will actually take rather than what was asked for, because
/// neither side is held to this machine: a deployment can name more cores than the host has, and a
/// row settled on one host outlives a move to a smaller one. Answering the number that will not run
/// would offer a screen a value it cannot send back (BR-ED2-005).
/// </para>
/// </summary>
public sealed record EncodeAutoRunStanding(bool Automatically, int MostCores, bool Stored, DateTime? UpdatedAt)
{
    public static EncodeAutoRunStanding Over(EncodeAutoRun? held, EncodeSettings deployed, MachineSettings machine)
    {
        ArgumentNullException.ThrowIfNull(deployed);

        int cap = EncodeAutoRun.CoresOn(machine);

        return held is null
            ? new EncodeAutoRunStanding(deployed.Automatically, Within(deployed.MostCores, cap), false, null)
            : new EncodeAutoRunStanding(held.Automatically, Within(held.MostCores, cap), true, held.UpdatedAt);
    }

    private static int Within(int mostCores, int cap) => Math.Clamp(mostCores, EncodeAutoRun.FewestCores, cap);
}
