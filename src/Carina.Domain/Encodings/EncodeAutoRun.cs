using Carina.Domain.Base;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

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

    public static bool Fits(int mostCores, MachineSettings machine)
        => mostCores >= FewestCores && mostCores <= CoresOn(machine);

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

    public int CoresARunTakes(MachineSettings machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return Math.Min(MostCores, machine.Cores);
    }

    private static int Within(int mostCores, int cap) => Math.Clamp(mostCores, EncodeAutoRun.FewestCores, cap);
}
