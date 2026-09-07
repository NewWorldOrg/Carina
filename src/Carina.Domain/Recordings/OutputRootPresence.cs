using Carina.Contracts;

namespace Carina.Domain.Recordings;

public enum RootAbsence
{
    Undeclared = 1,

    OutOfReach = 2,

    HoldsNothing = 3,
}

public static class OutputRootPresence
{
    public static RootAbsence? Missing(
        IReadOnlyList<StorageRootDto>? declared,
        OutputRoot root,
        bool reachable,
        IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(paths);

        if (!StorageRoots.Declares(declared, root.Value))
        {
            return RootAbsence.Undeclared;
        }

        if (!reachable)
        {
            return RootAbsence.OutOfReach;
        }

        return paths.Count == 0 ? RootAbsence.HoldsNothing : null;
    }
}
