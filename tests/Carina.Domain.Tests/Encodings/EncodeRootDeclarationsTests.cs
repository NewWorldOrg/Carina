using Carina.Contracts;
using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeRootDeclarationsTests
{
    private static readonly IReadOnlyList<StorageRootDto> DriverDeclares =
    [
        new() { Name = "primary", FreeBytes = 1, TotalBytes = 2, Writable = true },
        new() { Name = "bulk", FreeBytes = 3, TotalBytes = 4, Writable = false },
    ];

    [Fact(DisplayName = "BR-EV-001: the declared set is the driver's roots followed by the ones this process holds")]
    public void TheDeclaredSetIsTheDriversRootsFollowedByTheOnesThisProcessHolds()
    {
        DeclaredOutputRoots merged = EncodeRootDeclarations.Merged(
            DriverDeclares,
            [new StorageRootDto { Name = "encodes", FreeBytes = 5, TotalBytes = 6, Writable = true }]);

        Assert.Equal(["primary", "bulk", "encodes"], merged.Declared.Select(root => root.Name));
        Assert.Equal(5, merged.Declared[2].FreeBytes);
        Assert.Empty(merged.Shadowed);
    }

    [Fact(DisplayName = "BR-EV-001: a name both sides declare stays the driver's and is reported")]
    public void ANameBothSidesDeclareStaysTheDriversAndIsReported()
    {
        DeclaredOutputRoots merged = EncodeRootDeclarations.Merged(
            DriverDeclares,
            [
                new StorageRootDto { Name = "primary", FreeBytes = 99, TotalBytes = 99, Writable = true },
                new StorageRootDto { Name = "encodes", FreeBytes = 5, TotalBytes = 6, Writable = true },
            ]);

        Assert.Equal(["primary", "bulk", "encodes"], merged.Declared.Select(root => root.Name));
        Assert.Equal(1, merged.Declared[0].FreeBytes);
        Assert.Equal(["primary"], merged.Shadowed);
    }

    [Fact(DisplayName = "BR-EV-001: a driver that declares nothing leaves only what this process holds")]
    public void ADriverThatDeclaresNothingLeavesOnlyWhatThisProcessHolds()
    {
        DeclaredOutputRoots merged = EncodeRootDeclarations.Merged(
            [],
            [new StorageRootDto { Name = "encodes", FreeBytes = 5, TotalBytes = 6, Writable = true }]);

        Assert.Equal(["encodes"], merged.Declared.Select(root => root.Name));
    }

    [Fact]
    public void AProcessThatHoldsNothingAddsNothing()
    {
        DeclaredOutputRoots merged = EncodeRootDeclarations.Merged(DriverDeclares, []);

        Assert.Equal(DriverDeclares, merged.Declared);
        Assert.Empty(merged.Shadowed);
    }
}
