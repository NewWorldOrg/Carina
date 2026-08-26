using Carina.Contracts;

namespace Carina.Domain.Recordings;

public static class DiskPrecheck
{
    public static DiskPrecheckVerdict Weigh(
        OutputRoot root,
        IReadOnlyList<StorageRootDto>? roots,
        RecordingDemand starting,
        IReadOnlyList<RecordingDemand> alreadyRunning,
        DateTime asOf)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(starting);
        ArgumentNullException.ThrowIfNull(alreadyRunning);

        Int128 estimate = starting.HeaviestBytes(asOf);
        int weighed = 1;

        foreach (RecordingDemand running in alreadyRunning)
        {
            estimate += running.HeaviestBytes(asOf);
            weighed++;
        }

        StorageRootDto? declared = StorageRoots.Find(roots, root.Value);

        return DiskPrecheckVerdict.Of(
            Shortfall(roots, declared, estimate),
            estimate,
            declared?.FreeBytes ?? 0,
            weighed);
    }

    private static DiskShortfall? Shortfall(
        IReadOnlyList<StorageRootDto>? roots,
        StorageRootDto? declared,
        Int128 estimate)
    {
        if (roots is null)
        {
            return DiskShortfall.RootsUnknown;
        }

        if (declared is null)
        {
            return DiskShortfall.RootUndeclared;
        }

        if (declared.TotalBytes <= 0)
        {
            return DiskShortfall.RootUnmeasured;
        }

        if (!declared.Writable)
        {
            return DiskShortfall.RootNotWritable;
        }

        if (declared.FreeBytes <= 0)
        {
            return DiskShortfall.NoRoomLeft;
        }

        return estimate > declared.FreeBytes ? DiskShortfall.ShortOfTheEstimate : null;
    }
}
