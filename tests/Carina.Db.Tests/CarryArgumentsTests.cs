using Carina.Domain.Migration;

namespace Carina.Db.Tests;

public sealed class CarryArgumentsTests
{
    [Fact]
    public void ARunIsARehearsalUnlessItIsAskedToBeReal()
    {
        Assert.True(CarryArguments.TryRead(["--carry", "--from", "/a", "--into", "/b"], out CarryArguments? read, out _));
        Assert.NotNull(read);
        Assert.Equal(MigrationPass.Rehearsal, read.Pass);
    }

    [Fact]
    public void AskingForItToBeRealIsWhatMakesItReal()
    {
        Assert.True(CarryArguments.TryRead(
            ["--carry", "--from", "/a", "--into", "/b", "--for-real"],
            out CarryArguments? read,
            out _));
        Assert.NotNull(read);
        Assert.Equal(MigrationPass.ForReal, read.Pass);
    }

    [Fact]
    public void TheNewRootIsNamedByTheDirectoryItIs()
    {
        Assert.True(CarryArguments.TryRead(
            ["--carry", "--from", "/a", "--into", "/disk/carried"],
            out CarryArguments? read,
            out _));
        Assert.NotNull(read);
        Assert.Equal("carried", read.Root.Value);
    }

    [Theory]
    [InlineData("--carry")]
    [InlineData("--carry", "--from", "/a")]
    [InlineData("--carry", "--into", "/b")]
    [InlineData("--carry", "--from", "/a", "--into", "/b", "--from", "/c")]
    [InlineData("--carry", "--from", "/a", "--into", "/b", "--rehearse")]
    [InlineData("--carry", "--from", "/a", "--into")]
    [InlineData("--carry", "--from", "a", "--into", "/b")]
    [InlineData("--carry", "--from", "/a", "--into", "b")]
    [InlineData("--carry", "--from", "/a", "--into", "/disk/carried too far")]
    public void AnythingElseIsRefusedWithAReason(params string[] args)
    {
        Assert.False(CarryArguments.TryRead(args, out CarryArguments? read, out string problem));
        Assert.Null(read);
        Assert.NotEmpty(problem);
    }
}
