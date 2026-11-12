using Carina.Api.Common;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;

namespace Carina.Api.Services;

public sealed record ProgrammeWatchAsked(
    ProgrammeId Programme,
    DateTime ProgrammeStartsAt,
    int? LeadMinutes,
    IReadOnlyList<Guid>? Destinations);

/// <summary>
/// Lists the programmes asked to be told of, asks for one and withdraws one. Asking and withdrawing take the
/// turn the writers of the destinations take, so neither crosses a destination being removed or a notice being sent.
/// </summary>
public sealed class NotificationWatchService(
    IProgrammeWatchRepository watches,
    IProgrammeRepository programmes,
    IBroadcastServiceRepository stations,
    INotificationDestinationRepository destinations,
    INotificationSettingsRepository settings,
    INotificationRuleRepository rules,
    IAtomicWrite writes,
    TimeProvider clock)
{
    private const string UnknownProgramme = "The guide holds no programme of that name starting then.";

    private const string Shadow =
        "That programme is carried as a shadow of a broadcast that belongs to another service, so it is not the entry to be told of.";

    private const string Begun = "Only a programme that has not begun is asked to be told of.";

    private const string NoneTurnedOn = "No destination is turned on. Register one, or turn one on, to be told of a programme.";

    private const string BadDestinations = "A programme is told to at least one destination, each of them registered and turned on.";

    private const string Already = "That programme is already asked to be told of.";

    private const string Unknown = "No programme asked to be told of is held under that id.";

    private const string Settled = "Only a programme that still waits to be told of is withdrawn.";

    private static readonly string BadLead =
        $"A programme is told of {string.Join(", ", NotificationSettings.LeadMinutesOffered)} minutes before it begins.";

    public async Task<ServiceResult<IReadOnlyList<ProgrammeWatch>>> ListAsync(CancellationToken cancellationToken)
        => ServiceResult<IReadOnlyList<ProgrammeWatch>>.Success(await watches.ListAsync(cancellationToken));

    public async Task<ServiceResult<ProgrammeWatch, NotificationFailure>> AskAsync(
        ProgrammeWatchAsked asked,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asked);

        Programme? announced = await programmes.FindAsync(asked.Programme, cancellationToken);

        if (announced is null || announced.StartsAt != asked.ProgrammeStartsAt)
        {
            return Failure(UnknownProgramme, NotificationFailure.NoSuchProgramme);
        }

        DateTime now = Now();
        int leadMinutes = asked.LeadMinutes
            ?? NotificationSettings.LeadMinutesOver(await settings.ReadAsync(cancellationToken));

        if (ProgrammeWatch.RefusalToAsk(announced, leadMinutes, now) is { } refusal)
        {
            return Refused(refusal);
        }

        BroadcastService? station = await stations.FindAsync(
            asked.Programme.NetworkId,
            asked.Programme.ServiceId,
            cancellationToken);
        WatchedBroadcast broadcast = WatchedBroadcast.Of(announced, station?.Name);

        return await writes.AllOrNothingAsync(
            async inner =>
            {
                await rules.TakeTurnAsync(inner);

                return await AskInTurnAsync(asked, broadcast, leadMinutes, now, inner);
            },
            cancellationToken);
    }

    public async Task<ServiceResult<ProgrammeWatch, NotificationFailure>> WithdrawAsync(
        ProgrammeWatchId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await writes.AllOrNothingAsync(
            async inner =>
            {
                await rules.TakeTurnAsync(inner);

                return await WithdrawInTurnAsync(id, inner);
            },
            cancellationToken);
    }

    private static ServiceResult<ProgrammeWatch, NotificationFailure> Failure(string message, NotificationFailure failure)
        => ServiceResult<ProgrammeWatch, NotificationFailure>.Failure(message, failure);

    private static ServiceResult<ProgrammeWatch, NotificationFailure> Refused(ProgrammeWatchRefusal refusal)
        => refusal switch
        {
            ProgrammeWatchRefusal.NoSuchProgramme => Failure(UnknownProgramme, NotificationFailure.NoSuchProgramme),
            ProgrammeWatchRefusal.Shadow => Failure(Shadow, NotificationFailure.ProgrammeIsAShadow),
            ProgrammeWatchRefusal.AlreadyBegun => Failure(Begun, NotificationFailure.AlreadyBegun),
            ProgrammeWatchRefusal.LeadNotOffered => Failure(BadLead, NotificationFailure.Refused),
            ProgrammeWatchRefusal.NoDestinationTurnedOn => Failure(NoneTurnedOn, NotificationFailure.NoDestination),
            ProgrammeWatchRefusal.DestinationsNotTurnedOn => Failure(BadDestinations, NotificationFailure.Refused),
            ProgrammeWatchRefusal.AlreadyAsked => Failure(Already, NotificationFailure.AlreadyAsked),
            _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "A programme is refused for a reason this service words."),
        };

    private static IReadOnlyList<NotificationDestinationId>? ToldTo(
        IReadOnlyList<Guid>? asked,
        IReadOnlyList<NotificationDestination> held)
    {
        IReadOnlyList<Guid> turnedOn = [.. held.Where(destination => destination.Enabled).Select(destination => destination.Id.Value)];
        IReadOnlyList<Guid> named = asked ?? turnedOn;

        return named.Count > 0 && named.All(turnedOn.Contains)
            ? [.. named.Distinct().Select(id => new NotificationDestinationId(id))]
            : null;
    }

    private async Task<ServiceResult<ProgrammeWatch, NotificationFailure>> AskInTurnAsync(
        ProgrammeWatchAsked asked,
        WatchedBroadcast broadcast,
        int leadMinutes,
        DateTime now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationDestination> held = await destinations.ListAsync(cancellationToken);

        if (asked.Destinations is null && !held.Any(destination => destination.Enabled))
        {
            return Failure(NoneTurnedOn, NotificationFailure.NoDestination);
        }

        if (ToldTo(asked.Destinations, held) is not { } toldTo)
        {
            return Failure(BadDestinations, NotificationFailure.Refused);
        }

        ProgrammeWatch? before = await watches.FindOfAsync(asked.Programme, cancellationToken);

        if (before is not null && before.StandsAt(now))
        {
            return Failure(Already, NotificationFailure.AlreadyAsked);
        }

        if (before is not null)
        {
            await watches.RemoveAsync(before, cancellationToken);
        }

        ProgrammeWatch watch = ProgrammeWatch.Ask(ProgrammeWatchId.New(), asked.Programme, broadcast, leadMinutes, toldTo, now);
        await watches.AddAsync(watch, cancellationToken);

        return ServiceResult<ProgrammeWatch, NotificationFailure>.Success(watch);
    }

    private async Task<ServiceResult<ProgrammeWatch, NotificationFailure>> WithdrawInTurnAsync(
        ProgrammeWatchId id,
        CancellationToken cancellationToken)
    {
        if (await watches.FindAsync(id, cancellationToken) is not { } held)
        {
            return Failure(Unknown, NotificationFailure.NoSuchWatch);
        }

        ProgrammeWatchWithdrawal withdrawal = held.Withdraw(Now());

        if (withdrawal is ProgrammeWatchWithdrawal.Settled)
        {
            return Failure(Settled, NotificationFailure.NotWaiting);
        }

        if (withdrawal is ProgrammeWatchWithdrawal.Withdrawn)
        {
            await watches.SaveAsync(held, cancellationToken);
        }

        return ServiceResult<ProgrammeWatch, NotificationFailure>.Success(held);
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
