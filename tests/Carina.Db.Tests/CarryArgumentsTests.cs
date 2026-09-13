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
    public void WhatARunCarriesIntoIsADirectoryAndTheArgumentsDoNotNameARootForIt()
    {
        Assert.True(CarryArguments.TryRead(
            ["--carry", "--from", "/a", "--into", "/disk/carina/recordings"],
            out CarryArguments? read,
            out _));
        Assert.NotNull(read);
        Assert.Equal("/disk/carina/recordings", read.Into);
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
    public void AnythingElseIsRefusedWithAReason(params string[] args)
    {
        Assert.False(CarryArguments.TryRead(args, out CarryArguments? read, out string problem));
        Assert.Null(read);
        Assert.NotEmpty(problem);
    }
}
