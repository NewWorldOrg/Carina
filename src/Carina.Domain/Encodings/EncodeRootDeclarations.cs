using Puppis.Contracts;

namespace Carina.Domain.Encodings;

public sealed record DeclaredOutputRoots(IReadOnlyList<StorageRootDto> Declared, IReadOnlyList<string> Shadowed);

public static class EncodeRootDeclarations
{
    public static DeclaredOutputRoots Merged(
        IReadOnlyList<StorageRootDto> driverDeclares,
        IReadOnlyList<StorageRootDto> thisProcessHolds)
    {
        ArgumentNullException.ThrowIfNull(driverDeclares);
        ArgumentNullException.ThrowIfNull(thisProcessHolds);

        List<StorageRootDto> declared = [.. driverDeclares];
        List<string> shadowed = [];

        foreach (StorageRootDto held in thisProcessHolds)
        {
            ArgumentNullException.ThrowIfNull(held);

            if (StorageRoots.Declares(driverDeclares, held.Name))
            {
                shadowed.Add(held.Name);

                continue;
            }

            declared.Add(held);
        }

        return new DeclaredOutputRoots(declared, shadowed);
    }
}
