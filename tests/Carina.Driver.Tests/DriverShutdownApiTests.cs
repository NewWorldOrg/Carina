using System.Net;

using Carina.Contracts;
using Carina.Driver.Sessions;

namespace Carina.Driver.Tests;

public sealed class DriverShutdownApiTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static CancellationToken Soon() => new CancellationTokenSource(Patience).Token;

    private static async Task<DriverProblem?> Refusal(HttpResponseMessage response) =>
        await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

    [Fact]
    public async Task AskingAnIdleDriverToStopIsAcceptedAndPutsItIntoDraining()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var response = await client.PostAsync(DriverEndpoints.Shutdown, null, Soon());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(driver.Service<TunerSessionManager>().IsDraining);
    }

    [Fact]
    public async Task TheAnswerNamesTheInstanceThatIsGoingAwayAndHowLongItMayTake()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var greeted = await client.GetAsync(DriverEndpoints.Health, Soon());
        var hello = await DriverUnderTest.Read(greeted, DriverJson.Context.DriverHello);

        using var response = await client.PostAsync(DriverEndpoints.Shutdown, null, Soon());
        var accepted = await DriverUnderTest.Read(
            response,
            DriverJson.Context.DriverShutdownDto
        );

        Assert.NotNull(accepted);
        Assert.Equal(hello!.InstanceId, accepted.InstanceId);
        Assert.Equal(
            (int)driver.Service<TunerSessionManager>().ShutdownBudget.TotalSeconds,
            accepted.BudgetSeconds
        );
        Assert.NotEqual(default, accepted.AcceptedAt);
    }

    [Fact]
    public async Task ADriverHoldingARecordingRefusesToStopAndSaysWhichRecordingHoldsIt()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording("held", DateTimeOffset.UtcNow.AddMinutes(10))
            ),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var response = await client.PostAsync(DriverEndpoints.Shutdown, null, Soon());
        var refusal = await Refusal(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("recordingInProgress", refusal!.Title);
        Assert.Contains("held", string.Join(" ", refusal.Problems), StringComparison.Ordinal);
        Assert.False(driver.Service<TunerSessionManager>().IsDraining);
    }

    [Fact]
    public async Task ARefusedShutdownLeavesTheRecordingItProtectedRunning()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording("keeps-going", DateTimeOffset.UtcNow.AddMinutes(10))
            ),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var refused = await client.PostAsync(DriverEndpoints.Shutdown, null, Soon());

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        using var listed = await client.GetAsync(DriverEndpoints.Sessions, Soon());
        var sessions = await DriverUnderTest.Read(
            listed,
            DriverJson.Context.IReadOnlyListSessionSnapshot
        );

        var still = Assert.Single(sessions!);

        Assert.Equal("keeps-going", still.SessionId.Value);
        Assert.Equal(SessionState.Active, still.State);
    }

    [Fact]
    public async Task ADriverThatIsAlreadyDrainingTakesTheRequestAsDoneRatherThanRefusingIt()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        driver.Service<TunerSessionManager>().EnterDraining();

        using var response = await client.PostAsync(DriverEndpoints.Shutdown, null, Soon());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task AnAcceptedShutdownStopsTakingNewSessionsBeforeItAnswers()
    {
        await using var driver = await DriverUnderTest.Start();
        using var client = driver.Client();

        using var accepted = await client.PostAsync(DriverEndpoints.Shutdown, null, Soon());

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);

        var refusal = driver.Service<TunerSessionManager>().Begin(
            DriverUnderTest.Live("latecomer")
        );

        Assert.False(refusal.TryGetSession(out _));
        Assert.Equal(SessionRefusal.Draining, refusal.Refusal);
    }

    [Fact]
    public void ADriverThatStopsOnRequestSaysSoInItsGreeting()
    {
        Assert.Contains(
            DriverCapabilities.GracefulShutdown,
            Carina.Driver.Ipc.DriverGreeting.Capabilities
        );
    }
}
