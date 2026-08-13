using Carina.Contracts;
using Carina.Domain.Driver;

namespace Carina.Domain.Tests;

public sealed class DriverCallTests
{
    [Fact]
    public void AReachedCallCarriesItsValue()
    {
        var call = DriverCall<string>.Reached("hello");

        Assert.Equal(DriverCallOutcome.Reached, call.Outcome);
        Assert.True(call.TryGetValue(out var value));
        Assert.Equal("hello", value);
    }

    [Fact]
    public void AReachedCallMayCarryNoBody()
    {
        var call = DriverCall<string>.Reached(null);

        Assert.Equal(DriverCallOutcome.Reached, call.Outcome);
        Assert.False(call.TryGetValue(out _));
    }

    [Fact]
    public void ARefusalCarriesTheProblem()
    {
        var call = DriverCall<string>.Refused(new DriverProblem("deviceBusy", ["taken"]));

        Assert.Equal(DriverCallOutcome.Refused, call.Outcome);
        Assert.Equal("deviceBusy", call.Problem?.Title);
        Assert.False(call.TryGetValue(out _));
    }

    [Fact]
    public void ARefusalRequiresAProblem()
    {
        Assert.Throws<ArgumentNullException>(() => DriverCall<string>.Refused(null!));
    }

    [Fact]
    public void AnUnreachableDriverCarriesTheFailure()
    {
        var call = DriverCall<string>.Unreachable("connection refused");

        Assert.Equal(DriverCallOutcome.Unreachable, call.Outcome);
        Assert.Equal("connection refused", call.Failure);
        Assert.False(call.TryGetValue(out _));
    }

    [Fact]
    public void AnUnreachableDriverRequiresAFailure()
    {
        Assert.Throws<ArgumentException>(() => DriverCall<string>.Unreachable(" "));
    }
}
