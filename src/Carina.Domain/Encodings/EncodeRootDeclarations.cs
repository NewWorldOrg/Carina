using Carina.Contracts;

namespace Carina.Domain.Encodings;

/// <summary>
/// The set of output roots a destination may name: what the driver declares, and after it what
/// this process holds for writing. A name both declare stays the driver's and is reported, so
/// nothing is ever written into a root whose name means two places.
/// </summary>
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
