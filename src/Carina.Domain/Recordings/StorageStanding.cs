using Carina.Contracts;

namespace Carina.Domain.Recordings;

public sealed record RootDemand(OutputRoot Root, RecordingDemand Demand);

public sealed record StorageRootStanding(
    string Name,
    long FreeBytes,
    long TotalBytes,
    bool Writable,
    Int128 CommittedBytes,
    int RecordingsInFlight,
    DiskShortfall? Shortfall);

public static class StorageStanding
{
    public static IReadOnlyList<StorageRootStanding> Of(
        IReadOnlyList<StorageRootDto> declared,
        IReadOnlyList<RootDemand> running,
        DateTime asOf)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(running);

        List<StorageRootStanding> standing = [];
        HashSet<string> answered = new(StringComparer.Ordinal);

        foreach (StorageRootDto root in declared)
        {
            ArgumentNullException.ThrowIfNull(root);

            if (!answered.Add(root.Name))
            {
                throw new ArgumentException(
                    $"A driver declares each output root once, so '{root.Name}' cannot be declared twice.",
                    nameof(declared));
            }

            standing.Add(Of(root.Name, declared, root, running, asOf));
        }

        foreach (RootDemand demand in running)
        {
            ArgumentNullException.ThrowIfNull(demand);

            if (answered.Add(demand.Root.Value))
            {
                standing.Add(Of(demand.Root.Value, declared, null, running, asOf));
            }
        }

        return standing;
    }

    private static StorageRootStanding Of(
        string name,
        IReadOnlyList<StorageRootDto> declared,
        StorageRootDto? root,
        IReadOnlyList<RootDemand> running,
        DateTime asOf)
    {
        Int128 committed = Int128.Zero;
        int inFlight = 0;

        foreach (RootDemand demand in running)
        {
            ArgumentNullException.ThrowIfNull(demand);

            if (string.Equals(demand.Root.Value, name, StringComparison.Ordinal))
            {
                committed += demand.Demand.HeaviestBytes(asOf);
                inFlight++;
            }
        }

        return new StorageRootStanding(
            name,
            root?.FreeBytes ?? 0,
            root?.TotalBytes ?? 0,
            root?.Writable ?? false,
            committed,
            inFlight,
            DiskPrecheck.Shortfall(declared, root, committed));
    }
}
