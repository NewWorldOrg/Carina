using System.Net;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Sessions;

namespace Carina.Driver.Tests;

public sealed class DriverRestartApiTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static CancellationToken Soon() => new CancellationTokenSource(Patience).Token;

    private static async Task<DriverProblem?> Refusal(HttpResponseMessage response) =>
        await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

    [Fact]
    public async Task AskingAnIdleDriverToRestartIsAcceptedAndPutsItIntoDraining()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.PostAsync(DriverEndpoints.Restart, null, Soon());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(driver.Service<TunerSessionManager>().IsDraining);
    }

    [Fact]
    public async Task TheAnswerNamesTheInstanceThatIsGoingAwayAndHowLongItMayTake()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage greeted = await client.GetAsync(DriverEndpoints.Health, Soon());
        DriverHello? hello = await DriverUnderTest.Read(greeted, DriverJson.Context.DriverHello);

        using HttpResponseMessage response = await client.PostAsync(DriverEndpoints.Restart, null, Soon());
        DriverRestartDto? accepted = await DriverUnderTest.Read(
            response,
            DriverJson.Context.DriverRestartDto
        );

        TunerSessionManager manager = driver.Service<TunerSessionManager>();

        Assert.NotNull(accepted);
        Assert.Equal(hello!.InstanceId, accepted.InstanceId);
        Assert.Equal((int)manager.HardStopBudget.TotalSeconds, accepted.BudgetSeconds);
        Assert.True(accepted.BudgetSeconds < (int)manager.ShutdownBudget.TotalSeconds);
        Assert.NotEqual(default, accepted.AcceptedAt);
    }

    [Fact]
    public async Task ARestartTakenOnRequestLeavesWithTheCodeForAnOrderlyStopRatherThanAFault()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        DriverStopRequest stopRequest = driver.Service<DriverStopRequest>();

        Assert.False(stopRequest.WasAsked);
        Assert.Equal(
            DriverStartup.StoppedEarlyExitCode,
            DriverStartup.ExitCodeFor(stopRequest.WasAsked)
        );

        using HttpResponseMessage response = await client.PostAsync(DriverEndpoints.Restart, null, Soon());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(stopRequest.WasAsked);
        Assert.Equal(0, DriverStartup.ExitCodeFor(stopRequest.WasAsked));
    }

    [Fact]
    public async Task ADrainingDriverStillRefusesWhileARecordingItLingersForIsRunning()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording("lingered-for", DateTimeOffset.UtcNow.AddMinutes(10))
            ),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        driver.Service<TunerSessionManager>().EnterDraining();

        using HttpResponseMessage response = await client.PostAsync(DriverEndpoints.Restart, null, Soon());
        DriverProblem? refusal = await Refusal(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("recordingInProgress", refusal!.Title);
        Assert.Contains(
            "lingered-for",
            string.Join(" ", refusal.Problems),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ADriverHoldingARecordingRefusesToRestartAndSaysWhichRecordingHoldsIt()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording("held", DateTimeOffset.UtcNow.AddMinutes(10))
            ),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using HttpResponseMessage response = await client.PostAsync(DriverEndpoints.Restart, null, Soon());
        DriverProblem? refusal = await Refusal(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("recordingInProgress", refusal!.Title);
        Assert.Contains("held", string.Join(" ", refusal.Problems), StringComparison.Ordinal);
        Assert.False(driver.Service<TunerSessionManager>().IsDraining);
        Assert.False(driver.Service<DriverStopRequest>().WasAsked);
    }

    [Fact]
    public async Task ARefusedRestartLeavesTheRecordingItProtectedRunning()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording("keeps-going", DateTimeOffset.UtcNow.AddMinutes(10))
            ),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using HttpResponseMessage refused = await client.PostAsync(DriverEndpoints.Restart, null, Soon());

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        using HttpResponseMessage listed = await client.GetAsync(DriverEndpoints.Sessions, Soon());
        IReadOnlyList<SessionSnapshot>? sessions = await DriverUnderTest.Read(
            listed,
            DriverJson.Context.IReadOnlyListSessionSnapshot
        );

        SessionSnapshot still = Assert.Single(sessions!);

        Assert.Equal("keeps-going", still.SessionId.Value);
        Assert.Equal(SessionState.Active, still.State);
    }

    [Fact]
    public async Task ADriverThatIsAlreadyDrainingTakesTheRequestAsDoneRatherThanRefusingIt()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        driver.Service<TunerSessionManager>().EnterDraining();

        using HttpResponseMessage response = await client.PostAsync(DriverEndpoints.Restart, null, Soon());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task AnAcceptedRestartStopsTakingNewSessionsBeforeItAnswers()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage accepted = await client.PostAsync(DriverEndpoints.Restart, null, Soon());

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);

        SessionStart refusal = driver.Service<TunerSessionManager>().Begin(
            DriverUnderTest.Live("latecomer")
        );

        Assert.False(refusal.TryGetSession(out _));
        Assert.Equal(SessionRefusal.Draining, refusal.Refusal);
    }

    [Fact]
    public void ADriverThatRestartsOnRequestSaysSoInItsGreeting()
    {
        Assert.Contains(
            DriverCapabilities.GracefulRestart,
            Carina.Driver.Ipc.DriverGreeting.Capabilities
        );
    }
}
