using Carina.Domain.Base;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

/// <summary>
/// The one row that says how the queue runs when nobody asked: whether a recording that ends is
/// queued at all, and how many of the machine's cores a run may take. No row is held until somebody
/// settles them. <see cref="Subject"/> states what the auto-run takes and is not a setting.
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
/// settings where there is not. <see cref="Stored"/> tells the two apart.
/// <see cref="MostCores"/> is what a run will actually take on this machine, not what was asked for.
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
