using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Encodings;
using Carina.Domain.Events;
using Carina.Domain.Machines;

namespace Carina.Api.Services;

public sealed record EncodeAutoRunReading(EncodeAutoRunStanding Standing, EncodeUnaskedStanding WhereArtefactsGo);

/// <summary>
/// How the queue runs when nobody asked, read and settled. Two things are a person's to choose —
/// whether a recording that ends is queued at all, and how many cores a run may take — and both are
/// answered together with what the machine has, so a screen can offer the choice without knowing
/// anything about this host. What the auto-run takes is not a choice (BR-ED2-004) and is stated
/// rather than asked for; nor is giving way to someone watching, which no setting reaches. Each
/// answer also says whether the destinations and profiles defined let the auto-run settle where an
/// artefact goes.
/// </summary>
public sealed class EncodeAutoRunService(
    IEncodeAutoRunRepository rows,
    IEncodeAutoRunReader standings,
    IEncodeDestinationRepository destinations,
    IEncodeProfileRepository profiles,
    MachineSettings machine,
    IAppEventPublisher events,
    TimeProvider clock)
{
    public async Task<ServiceResult<EncodeAutoRunReading>> ReadAsync(CancellationToken cancellationToken)
        => ServiceResult<EncodeAutoRunReading>.Success(await ReadingAsync(cancellationToken));

    public async Task<ServiceResult<EncodeAutoRunReading>> SettleAsync(
        bool? automatically,
        int? mostCores,
        CancellationToken cancellationToken)
    {
        if (automatically is not { } running)
        {
            return ServiceResult<EncodeAutoRunReading>.Failure(
                "automatically: expected whether a recording that ends is queued for encoding without anyone asking.");
        }

        if (mostCores is not { } cores)
        {
            return ServiceResult<EncodeAutoRunReading>.Failure(
                "mostCores: expected how many of this machine's cores a run may take.");
        }

        if (cores < EncodeAutoRun.FewestCores || cores > Cores)
        {
            return ServiceResult<EncodeAutoRunReading>.Failure(
                $"mostCores: expected a whole number of cores from {EncodeAutoRun.FewestCores} to {Cores}, "
                + "which is how many this machine has.");
        }

        await rows.SaveAsync(EncodeAutoRun.Settled(running, cores, Now()), cancellationToken);

        events.Signal(AppEventName.EncodeJobs);

        return ServiceResult<EncodeAutoRunReading>.Success(await ReadingAsync(cancellationToken));
    }

    public int Cores => EncodeAutoRun.CoresOn(machine);

    private async Task<EncodeAutoRunReading> ReadingAsync(CancellationToken cancellationToken)
    {
        EncodeUnasked unasked = EncodeUnasked.Of(
            await destinations.ListAsync(cancellationToken),
            await profiles.ListAsync(cancellationToken));

        return new EncodeAutoRunReading(await standings.ReadAsync(cancellationToken), unasked.Standing);
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
