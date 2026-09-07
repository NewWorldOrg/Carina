using Carina.Contracts;

namespace Carina.Domain.Recordings;

public enum RootAbsence
{
    Undeclared = 1,

    OutOfReach = 2,

    HoldsNothingBeside = 3,
}

public static class OutputRootPresence
{
    public static RootAbsence? Missing(
        IReadOnlyList<StorageRootDto>? declared,
        OutputRoot root,
        bool reachable,
        IReadOnlyList<string> paths,
        RecordingId mine)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(mine);

        if (!StorageRoots.Declares(declared, root.Value))
        {
            return RootAbsence.Undeclared;
        }

        if (!reachable)
        {
            return RootAbsence.OutOfReach;
        }

        return paths.Any(path => !Names(path, mine)) ? null : RootAbsence.HoldsNothingBeside;
    }

    private static bool Names(string path, RecordingId mine)
        => Leaf(path).StartsWith(mine.Wire, StringComparison.Ordinal);

    private static string Leaf(string path) => path[(path.LastIndexOf('/') + 1)..];
}
