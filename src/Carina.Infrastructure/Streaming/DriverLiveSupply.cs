using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;
using Carina.Domain.Streaming;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Streaming;

public sealed class DriverLiveSupply(
    IDriverClient driver,
    IDriverStatusReader status,
    IServiceScopeFactory scopes) : ILiveSupply
{
    public const string NoStreamBecause = "no viewer stream could be opened on the session";

    public const string GivenUpBecause = "the viewer gave up before the stream was open";

    public async Task<LiveSupplyStart> OpenAsync(NetworkId network, ServiceId service, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(service);

        TuningResolution resolution = await ResolveAsync(network, service, cancellationToken);

        if (resolution.Tuning is not { } tuning)
        {
            return LiveSupplyStart.Refused(
                Refusal(resolution.Refusal),
                $"the service {network.Value}:{service.Value} cannot be tuned ({resolution.Refusal}).");
        }

        TuneParams tune = tuning.Typed();
        SessionId sessionId = LiveSessions.Fresh();

        DriverCall<SessionSnapshot> started = await driver.StartSessionAsync(
            new StartSessionRequest
            {
                SessionId = sessionId,
                Purpose = SessionPurpose.Live,
                Tuning = tune.ToLegacyRequest(),
                Tune = tune,
            },
            cancellationToken);

        if (!started.TryGetValue(out SessionSnapshot? session))
        {
            return Refused(started);
        }

        if (session.State is SessionState.Failed)
        {
            return LiveSupplyStart.Refused(
                LiveRefusal.WouldNotTune,
                session.FailureCause ?? session.FirstFault ?? "the driver could not tune this channel.");
        }

        DriverCall<Stream> opened;

        try
        {
            opened = await driver.OpenSessionStreamAsync(sessionId, DriverEndpoints.ViewerSubscriber, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await driver.StopSessionAsync(sessionId, GivenUpBecause, CancellationToken.None);

            throw;
        }

        if (!opened.TryGetValue(out Stream? bytes))
        {
            await driver.StopSessionAsync(sessionId, NoStreamBecause, CancellationToken.None);

            return Refused(opened);
        }

        return LiveSupplyStart.Opened(new DriverTransportStream(sessionId, bytes, driver, status));
    }

    public static LiveRefusal Refusal(TuningRefusal refusal)
        => refusal switch
        {
            TuningRefusal.NoSuchService or TuningRefusal.NoSelectedChannel => LiveRefusal.NoSuchChannel,
            TuningRefusal.NoTunerForSystem => LiveRefusal.NoTunerFree,
            _ => LiveRefusal.DriverUnavailable,
        };

    public static LiveRefusal Refusal(DriverProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        return problem.Title switch
        {
            SessionRefusalTitles.NoDeviceFree or SessionRefusalTitles.DeviceBusy => LiveRefusal.NoTunerFree,
            SessionRefusalTitles.NoLock
                or SessionRefusalTitles.DeviceUnavailable
                or SessionRefusalTitles.FaultedDevice
                or SessionRefusalTitles.DisabledDevice
                or SessionRefusalTitles.NoDeviceOfThatKind
                or SessionRefusalTitles.WrongDeviceKind
                or SessionRefusalTitles.UnknownDevice => LiveRefusal.WouldNotTune,
            _ => LiveRefusal.DriverUnavailable,
        };
    }

    private static LiveSupplyStart Refused<T>(DriverCall<T> call)
        => call.Problem is { } problem
            ? LiveSupplyStart.Refused(Refusal(problem), string.Join(" ", problem.Problems.Prepend(problem.Title)))
            : LiveSupplyStart.Refused(LiveRefusal.DriverUnavailable, call.Failure ?? "the driver could not be reached.");

    private async Task<TuningResolution> ResolveAsync(NetworkId network, ServiceId service, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<IServiceTuningDirectory>()
            .ResolveTuningAsync(network, service, cancellationToken);
    }
}
