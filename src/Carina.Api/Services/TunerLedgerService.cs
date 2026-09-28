using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Reservations;

namespace Carina.Api.Services;

public sealed class TunerLedgerService(
    IDriverClient driver,
    ICandidateChannelRepository candidates,
    IRecalculationNotice notices,
    TimeProvider clock)
{
    public const string CapabilityMissingTitle = "capabilityMissing";

    public const string NoSuchTunerTitle = "noSuchTuner";

    public const string LedgerChangedTitle = "ledgerChanged";

    public async Task<ServiceResult<TunerLedgerView, TunerLedgerFailure>> ReadAsync(
        CancellationToken cancellationToken)
    {
        DriverCall<TunerLedgerDto> ledger = await driver.GetTunerLedgerAsync(cancellationToken);

        if (!ledger.TryGetValue(out TunerLedgerDto? document))
        {
            return Failed<TunerLedgerView, TunerLedgerDto>(ledger);
        }

        DriverCall<IReadOnlyList<TunerSnapshot>> tuners = await driver.GetTunersAsync(cancellationToken);

        return ServiceResult<TunerLedgerView, TunerLedgerFailure>.Success(
            Merge(document, tuners));
    }

    public async Task<ServiceResult<DetectedTunersView, TunerLedgerFailure>> DetectAsync(
        CancellationToken cancellationToken)
    {
        DriverCall<IReadOnlyList<DetectedDeviceDto>> detection = await driver.GetDetectedDevicesAsync(cancellationToken);

        if (!detection.TryGetValue(out IReadOnlyList<DetectedDeviceDto>? detected))
        {
            return Failed<DetectedTunersView, IReadOnlyList<DetectedDeviceDto>>(detection);
        }

        DriverCall<TunerLedgerDto> ledger = await driver.GetTunerLedgerAsync(cancellationToken);

        if (!ledger.TryGetValue(out TunerLedgerDto? document))
        {
            return Failed<DetectedTunersView, TunerLedgerDto>(ledger);
        }

        DriverCall<IReadOnlyList<TunerSnapshot>> tuners = await driver.GetTunersAsync(cancellationToken);

        return ServiceResult<DetectedTunersView, TunerLedgerFailure>.Success(
            Compare(detected, document, tuners.Value ?? []));
    }

    public async Task<ServiceResult<TunerLedgerView, TunerLedgerFailure>> ReplaceAsync(
        IReadOnlyList<TunerConfigEntry> wanted,
        string? expectedSavedHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wanted);

        if (wanted.Count == 0)
        {
            return ServiceResult<TunerLedgerView, TunerLedgerFailure>.Failure(
                "A ledger names every tuner it wants kept; emptying it is a separate, deliberate operation.",
                TunerLedgerFailure.EmptyLedger);
        }

        if (wanted.SelectMany(entry => entry.Validate()).ToArray() is { Length: > 0 } malformed)
        {
            return ServiceResult<TunerLedgerView, TunerLedgerFailure>.Failure(
                string.Join(" ", malformed),
                TunerLedgerFailure.Malformed);
        }

        DriverCall<TunerLedgerDto> replaced = await driver.ReplaceTunerLedgerAsync(
            wanted,
            expectedSavedHash,
            cancellationToken);

        if (!replaced.TryGetValue(out TunerLedgerDto? document))
        {
            return Failed<TunerLedgerView, TunerLedgerDto>(replaced);
        }

        await candidates.RequireRevalidationAsync(cancellationToken);

        notices.Nudge(RecalculationTrigger.TunerConfigurationChanged);

        DriverCall<IReadOnlyList<TunerSnapshot>> tuners = await driver.GetTunersAsync(cancellationToken);

        return ServiceResult<TunerLedgerView, TunerLedgerFailure>.Success(Merge(document, tuners));
    }

    /// <summary>
    /// Turns one satellite tuner's low-noise block power on or off in the saved ledger. Nothing else in the
    /// ledger is written, so no candidate is asked to prove itself again and no allocation is settled again.
    /// </summary>
    public async Task<ServiceResult<TunerLedgerView, TunerLedgerFailure>> SwitchLnbPowerAsync(
        string deviceId,
        bool on,
        CancellationToken cancellationToken)
    {
        DriverCall<TunerLedgerDto> switched = await driver.SwitchLnbPowerAsync(deviceId, on, cancellationToken);

        if (!switched.TryGetValue(out TunerLedgerDto? document))
        {
            return Failed<TunerLedgerView, TunerLedgerDto>(switched);
        }

        DriverCall<IReadOnlyList<TunerSnapshot>> tuners = await driver.GetTunersAsync(cancellationToken);

        return ServiceResult<TunerLedgerView, TunerLedgerFailure>.Success(Merge(document, tuners));
    }

    public async Task<ServiceResult<TunerSnapshot, TunerLedgerFailure>> ToggleAsync(
        string deviceId,
        bool disabled,
        CancellationToken cancellationToken)
    {
        DriverCall<TunerSnapshot> toggled = await driver.ToggleTunerAsync(deviceId, disabled, cancellationToken);

        if (!toggled.TryGetValue(out TunerSnapshot? snapshot))
        {
            return Failed<TunerSnapshot, TunerSnapshot>(toggled);
        }

        notices.Nudge(RecalculationTrigger.TunerConfigurationChanged);

        return ServiceResult<TunerSnapshot, TunerLedgerFailure>.Success(snapshot);
    }

    private TunerLedgerView Merge(TunerLedgerDto document, DriverCall<IReadOnlyList<TunerSnapshot>> tuners)
    {
        TunerObservations? observed = tuners.TryGetValue(out IReadOnlyList<TunerSnapshot>? snapshots)
            ? new TunerObservations(snapshots, clock.GetUtcNow())
            : null;

        return new TunerLedgerView(
            document.Tuners,
            document.SavedHash,
            document.LoadedHash,
            document.HasDrifted(),
            observed,
            observed is null ? Describe(tuners) : null);
    }

    private static DetectedTunersView Compare(
        IReadOnlyList<DetectedDeviceDto> detected,
        TunerLedgerDto document,
        IReadOnlyList<TunerSnapshot> observed)
    {
        Dictionary<string, DetectedDeviceDto> known =
            detected.ToDictionary(device => device.DeviceId, StringComparer.Ordinal);
        HashSet<string> kept = document.Tuners.Select(entry => entry.DeviceId).ToHashSet(StringComparer.Ordinal);

        return new DetectedTunersView(
            detected,
            [.. detected.Where(device => !kept.Contains(device.DeviceId)).Select(device => device.DeviceId)],
            [.. kept.Where(deviceId => !known.ContainsKey(deviceId)).Order(StringComparer.Ordinal)],
            [
                .. observed
                    .Where(tuner => kept.Contains(tuner.DeviceId))
                    .Where(tuner => known.TryGetValue(tuner.DeviceId, out DetectedDeviceDto? device)
                        && device.Detection is DeviceDetection.Detected
                        && !device.Kinds.Contains(tuner.Kind))
                    .Select(tuner => new TunerKindMismatch(
                        tuner.DeviceId,
                        tuner.Kind,
                        known[tuner.DeviceId].Kinds)),
            ]);
    }

    private static ServiceResult<TResult, TunerLedgerFailure> Failed<TResult, TCalled>(
        DriverCall<TCalled> call)
        => ServiceResult<TResult, TunerLedgerFailure>.Failure(Describe(call), FailureOf(call));

    private static TunerLedgerFailure FailureOf<T>(DriverCall<T> call)
    {
        if (call.Outcome is DriverCallOutcome.Unreachable)
        {
            return TunerLedgerFailure.DriverUnreachable;
        }

        return call.Problem?.Title switch
        {
            CapabilityMissingTitle => TunerLedgerFailure.CapabilityMissing,
            NoSuchTunerTitle => TunerLedgerFailure.NoSuchTuner,
            LedgerChangedTitle => TunerLedgerFailure.LedgerChanged,
            _ => TunerLedgerFailure.DriverRefused,
        };
    }

    private static string Describe<T>(DriverCall<T> call)
    {
        if (call.Failure is { } failure)
        {
            return failure;
        }

        if (call.Problem is not { } problem)
        {
            return "The driver answered without saying anything.";
        }

        return problem.Problems.Count == 0
            ? problem.Title
            : $"{problem.Title}: {string.Join(" ", problem.Problems)}";
    }
}
