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
    ILiveLeases leases,
    IServiceScopeFactory scopes) : ILiveSupply
{
    public const string NoStreamBecause = "no viewer stream could be opened on the session";

    public const string GivenUpBecause = "the viewer gave up before the stream was open";

    public const string NeverAnsweredBecause = "the driver never said whether this session was started";

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

        // Taken before the driver is told the id, so a session it holds is never a stray merely
        // because this call has not come back yet.
        leases.Take(sessionId);

        DriverCall<SessionSnapshot> started;

        try
        {
            started = await driver.StartSessionAsync(
                new StartSessionRequest
                {
                    SessionId = sessionId,
                    Purpose = SessionPurpose.Live,
                    Tuning = tune.ToLegacyRequest(),
                    Tune = tune,
                },
                cancellationToken);
        }
        catch (Exception)
        {
            await LetGoAsync(sessionId, NeverAnsweredBecause);

            throw;
        }

        if (!started.TryGetValue(out SessionSnapshot? session))
        {
            // A refusal the driver spelled out started nothing; a call that never arrived may have.
            if (started.Outcome is DriverCallOutcome.Unreachable)
            {
                await LetGoAsync(sessionId, NeverAnsweredBecause);
            }
            else
            {
                leases.LetGo(sessionId);
            }

            return await RefusedAsync(started, tune, cancellationToken);
        }

        if (session.State is SessionState.Failed)
        {
            leases.LetGo(sessionId);

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
            await LetGoAsync(sessionId, GivenUpBecause);

            throw;
        }

        if (!opened.TryGetValue(out Stream? bytes))
        {
            await LetGoAsync(sessionId, NoStreamBecause);

            return await RefusedAsync(opened, tune, cancellationToken);
        }

        return LiveSupplyStart.Opened(new DriverTransportStream(sessionId, bytes, driver, status, leases));
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

    public static LiveRefusalDetail TuneFailure(DriverProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        return problem.Title is SessionRefusalTitles.NoLock
            ? LiveRefusalDetail.Of(TuneFailureKind.NoLock)
            : LiveRefusalDetail.Unsaid;
    }

    public static LiveRefusalDetail Holding(IEnumerable<TunerSnapshot> tuners, TunerKind kind)
    {
        ArgumentNullException.ThrowIfNull(tuners);

        SessionPurpose[] purposes =
        [
            .. tuners
                .Where(tuner => tuner.Kind == kind)
                .Select(tuner => tuner.CurrentSession?.Purpose ?? SessionPurpose.Unspecified),
        ];

        if (purposes.Contains(SessionPurpose.Recording))
        {
            return LiveRefusalDetail.Of(LiveTunerHolder.ARecording);
        }

        return purposes.Contains(SessionPurpose.Live)
            ? LiveRefusalDetail.Of(LiveTunerHolder.AnotherViewer)
            : LiveRefusalDetail.Unsaid;
    }

    private async Task LetGoAsync(SessionId session, string because)
    {
        await driver.StopSessionAsync(session, because, CancellationToken.None);

        leases.LetGo(session);
    }

    private async Task<LiveSupplyStart> RefusedAsync<T>(
        DriverCall<T> call,
        TuneParams tune,
        CancellationToken cancellationToken)
    {
        if (call.Problem is not { } problem)
        {
            return LiveSupplyStart.Refused(
                LiveRefusal.DriverUnavailable,
                call.Failure ?? "the driver could not be reached.");
        }

        LiveRefusal refusal = Refusal(problem);
        string note = string.Join(" ", problem.Problems.Prepend(problem.Title));

        return refusal switch
        {
            LiveRefusal.WouldNotTune => LiveSupplyStart.Refused(refusal, note, TuneFailure(problem)),
            LiveRefusal.NoTunerFree => LiveSupplyStart.Refused(
                refusal,
                note,
                await HoldingAsync(tune.Kind, cancellationToken)),
            _ => LiveSupplyStart.Refused(refusal, note),
        };
    }

    private async Task<LiveRefusalDetail> HoldingAsync(TunerKind kind, CancellationToken cancellationToken)
    {
        DriverCall<IReadOnlyList<TunerSnapshot>> tuners = await driver.GetTunersAsync(cancellationToken);

        return tuners.TryGetValue(out IReadOnlyList<TunerSnapshot>? seen)
            ? Holding(seen, kind)
            : LiveRefusalDetail.Unsaid;
    }

    private async Task<TuningResolution> ResolveAsync(NetworkId network, ServiceId service, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<IServiceTuningDirectory>()
            .ResolveTuningAsync(network, service, cancellationToken);
    }
}
