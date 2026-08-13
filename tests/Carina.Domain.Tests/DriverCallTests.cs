using Carina.Contracts;
using Carina.Domain.Driver;

namespace Carina.Domain.Tests;

public sealed class DriverCallTests
{
    [Fact]
    public void AReachedCallCarriesItsValueAndStatus()
    {
        var call = DriverCall<string>.Reached("hello", 200);

        Assert.Equal(DriverCallOutcome.Reached, call.Outcome);
        Assert.Equal(200, call.StatusCode);
        Assert.True(call.TryGetValue(out var value));
        Assert.Equal("hello", value);
    }

    [Fact]
    public void AReachedCallMayCarryNoBody()
    {
        var call = DriverCall<string>.Reached(null, 202);

        Assert.Equal(DriverCallOutcome.Reached, call.Outcome);
        Assert.False(call.TryGetValue(out _));
    }

    [Fact]
    public void ARefusalCarriesTheProblemAndStatus()
    {
        var call = DriverCall<string>.Refused(409, new DriverProblem("deviceBusy", ["taken"]));

        Assert.Equal(DriverCallOutcome.Refused, call.Outcome);
        Assert.Equal(409, call.StatusCode);
        Assert.Equal("deviceBusy", call.Problem?.Title);
        Assert.False(call.TryGetValue(out _));
    }

    [Fact]
    public void ARefusalRequiresAProblem()
    {
        Assert.Throws<ArgumentNullException>(() => DriverCall<string>.Refused(409, null!));
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
