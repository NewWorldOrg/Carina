using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Tables;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Events;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

using Puppis.Contracts;

namespace Carina.Infrastructure.Collection;

public sealed record ProgrammesWritten(int Added, int Updated, int Discarded, int Clamped = 0);

public sealed class ProgrammeWriter(
    IProgrammeRepository programmes,
    IAtomicWrite writes,
    TimeProvider clock,
    IAppEventPublisher events,
    IRecalculationNotice notices)
{
    public async Task<ProgrammesWritten> WriteAsync(
        IReadOnlyList<EventInformationTable> tables,
        IReadOnlyList<ScheduledService> heardWhole,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(heardWhole);

        ProgrammesWritten written = await writes.AllOrNothingAsync(
            async token =>
            {
                DateTime at = clock.GetUtcNow().UtcDateTime;
                int discarded = 0;
                var gathered = new Dictionary<ProgrammeId, ProgrammeBroadcast>();

                foreach (EventInformationTable table in tables)
                {
                    discarded += table.DiscardedEvents;

                    foreach (DescribedEvent carried in table.Events)
                    {
                        ProgrammeBroadcast? broadcast = Read(table, carried);

                        if (broadcast is null || !broadcast.StartsWithinTheHorizonAt(at))
                        {
                            discarded++;

                            continue;
                        }

                        gathered[broadcast.Id] = gathered.TryGetValue(broadcast.Id, out ProgrammeBroadcast? seen)
                            ? seen.MergedWith(broadcast)
                            : broadcast;
                    }
                }

                ProgrammesAbsorbed absorbed = await programmes.AbsorbAsync(
                    [.. gathered.Values],
                    [.. heardWhole.Select(service => new ProgrammeService(service.NetworkId, service.ServiceId))],
                    at,
                    token);

                return new ProgrammesWritten(
                    absorbed.Added,
                    absorbed.Updated,
                    discarded,
                    gathered.Values.Count(Programme.Clamps));
            },
            cancellationToken);

        if (written.Added > 0 || written.Updated > 0 || written.Discarded > 0)
        {
            events.Signal(AppEventName.Programs);
        }

        if (written.Added > 0 || written.Updated > 0)
        {
            notices.Nudge(RecalculationTrigger.ProgrammesChanged);
        }

        return written;
    }

    private static ProgrammeBroadcast? Read(EventInformationTable table, DescribedEvent carried)
    {
        if (carried.EventId is < EventId.MinValue or > EventId.MaxValue)
        {
            return null;
        }

        ShortEventDescription? described = carried.Described;
        ExtendedEventDescription? detailed = carried.Detailed;

        return ProgrammeBroadcast.Announced(
            new ProgrammeId(
                new NetworkId(table.OriginalNetworkId),
                new ServiceId(table.ServiceId),
                new EventId(carried.EventId)),
            new TransportStreamId(table.TransportStreamId),
            carried.StartsAt.UtcDateTime,
            carried.EndsAt?.UtcDateTime,
            described?.Name ?? string.Empty,
            described?.Summary ?? string.Empty,
            Source(table),
            Running(carried.Status),
            [.. Groups(carried.Groupings)]) with
        {
            Genres = [.. carried.Genres.Select(genre => new ProgrammeGenre(genre.Kind, genre.Sort))],
            Items = detailed is null
                ? []
                : [.. detailed.Items.Select(item => new ProgrammeItem(item.Heading, item.Text))],
            HasSubtitles = carried.DataContents.Any(content => content.CarriesCaptions),
            Audio = AnnouncedAudio.Of(carried.AudioComponents),
            Sounds = AnnouncedAudio.Sounds(carried.AudioComponents),
            Video = AnnouncedVideo.ModeOf(carried.Components),
            Aspect = AnnouncedVideo.AspectOf(carried.Components),
        };
    }

    private static IEnumerable<AnnouncedEventGroup> Groups(IReadOnlyList<EventGrouping> groupings)
    {
        foreach (EventGrouping grouping in groupings)
        {
            if (Relation(grouping.Kind) is { } kind)
            {
                yield return new AnnouncedEventGroup(
                    kind,
                    [
                        .. grouping.Events.Select(carried => new AnnouncedEvent(null, carried.ServiceId, carried.EventId)),
                        .. grouping.Elsewhere.Select(carried =>
                            new AnnouncedEvent(carried.NetworkId, carried.ServiceId, carried.EventId)),
                    ]);
            }
        }
    }

    private static RelationKind? Relation(EventGroupKind kind)
        => kind switch
        {
            EventGroupKind.Shared => RelationKind.Shared,
            EventGroupKind.Relayed or EventGroupKind.RelayedFromAnotherNetwork => RelationKind.Relayed,
            EventGroupKind.Moved or EventGroupKind.MovedToAnotherNetwork => RelationKind.Moved,
            _ => null,
        };

    private static ProgrammeRunning Running(RunningStatus status)
        => status switch
        {
            RunningStatus.NotRunning => ProgrammeRunning.NotRunning,
            RunningStatus.StartsInSeconds => ProgrammeRunning.StartsInSeconds,
            RunningStatus.Pausing => ProgrammeRunning.Pausing,
            RunningStatus.Running => ProgrammeRunning.Running,
            RunningStatus.OffAir => ProgrammeRunning.OffAir,
            _ => ProgrammeRunning.Undetermined,
        };

    private static ProgrammeSource Source(EventInformationTable table)
    {
        if (table.IsPresentFollowing)
        {
            return ProgrammeSource.PresentFollowing;
        }

        return table.TableId >= EventInformationTable.FirstScheduleActualTableId + 8
            ? ProgrammeSource.ScheduleExtended
            : ProgrammeSource.ScheduleBasic;
    }
}
