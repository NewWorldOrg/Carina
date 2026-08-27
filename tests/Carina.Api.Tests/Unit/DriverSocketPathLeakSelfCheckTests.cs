namespace Carina.Api.Tests.Unit;

public sealed class DriverSocketPathLeakSelfCheckTests
{
    private const string Socket = "/var/run/carina-elsewhere/driver-of-this-host.sock";

    [Fact]
    public void DetectsAnAnswerThatCarriesTheWholePath()
    {
        AnsweredSurface answer = new("GET /api/tuners", 503, $$"""{"message":"cannot reach {{Socket}}"}""");

        Assert.Equal(
            ["GET /api/tuners answered 503 naming where the socket is"],
            DriverSocketPathLeak.In([answer], Socket));
    }

    [Fact]
    public void DetectsAnAnswerThatCarriesOnlyTheRoomTheSocketIsIn()
    {
        AnsweredSurface answer = new("GET /api/storage", 503, """{"message":"/var/run/carina-elsewhere is not there"}""");

        Assert.Single(DriverSocketPathLeak.In([answer], Socket));
    }

    [Fact]
    public void DetectsAnAnswerThatCarriesOnlyTheNameOfTheSocket()
    {
        AnsweredSurface answer = new("GET /api/storage", 503, """{"message":"driver-of-this-host.sock is missing"}""");

        Assert.Single(DriverSocketPathLeak.In([answer], Socket));
    }

    [Fact]
    public void LeavesAnAnswerThatSaysWhatHappenedWithoutSayingWhere()
    {
        AnsweredSurface answer = new(
            "GET /api/tuners",
            503,
            """{"message":"The driver's socket could not be reached (ConnectionRefused)."}""");

        Assert.Empty(DriverSocketPathLeak.In([answer], Socket));
    }

    [Fact]
    public void ReadsEveryAnswerItIsHandedRatherThanStoppingAtTheFirst()
    {
        AnsweredSurface clean = new("GET /api/health", 200, """{"status":true}""");
        AnsweredSurface leaking = new("GET /api/tuners", 503, $$"""{"message":"{{Socket}}"}""");

        Assert.Single(DriverSocketPathLeak.In([clean, leaking], Socket));
        Assert.Empty(DriverSocketPathLeak.In([clean], Socket));
    }

    [Fact]
    public void TheThreeWaysAPathCanBeNamedAreTheOnesTheRuleLooksFor()
    {
        Assert.Equal(
            [Socket, "/var/run/carina-elsewhere", "driver-of-this-host.sock"],
            DriverSocketPathLeak.WhereItIsNamed(Socket));
    }
}
