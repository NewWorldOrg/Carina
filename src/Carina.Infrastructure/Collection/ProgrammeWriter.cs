using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Tables;
using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Events;
using Carina.Domain.Programmes;

namespace Carina.Infrastructure.Collection;

public sealed record ProgrammesWritten(int Added, int Updated, int Discarded);

public sealed class ProgrammeWriter(
    IProgrammeRepository programmes,
    IAtomicWrite writes,
    TimeProvider clock,
    IAppEventPublisher events)
{
    public async Task<ProgrammesWritten> WriteAsync(
        IReadOnlyList<EventInformationTable> tables,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tables);

        ProgrammesWritten written = await writes.AllOrNothingAsync(
            async token =>
            {
                DateTime at = clock.GetUtcNow().UtcDateTime;
                int added = 0;
                int updated = 0;
                int discarded = 0;
                var gathered = new Dictionary<ProgrammeId, ProgrammeBroadcast>();

                foreach (EventInformationTable table in tables)
                {
                    discarded += table.DiscardedEvents;

                    foreach (DescribedEvent carried in table.Events)
                    {
                        ProgrammeBroadcast? broadcast = Read(table, carried);

                        if (broadcast is null)
                        {
                            discarded++;

                            continue;
                        }

                        gathered[broadcast.Id] = gathered.TryGetValue(broadcast.Id, out ProgrammeBroadcast? seen)
                            ? Merged(seen, broadcast)
                            : broadcast;
                    }
                }

                foreach (ProgrammeBroadcast broadcast in gathered.Values)
                {
                    Programme? held = await programmes.FindAsync(broadcast.Id, token);

                    if (held is null)
                    {
                        await programmes.AddAsync(Programme.Discover(broadcast, at), token);
                        added++;

                        continue;
                    }

                    if (held.Absorb(broadcast, at))
                    {
                        held.MarkRevision(await programmes.NextRevisionAsync(token));

                        await programmes.SaveAsync(held, token);
                        updated++;
                    }
                }

                return new ProgrammesWritten(added, updated, discarded);
            },
            cancellationToken);

        if (written.Added > 0 || written.Updated > 0 || written.Discarded > 0)
        {
            events.Signal(AppEventName.Programs);
        }

        return written;
    }

    private static ProgrammeBroadcast Merged(ProgrammeBroadcast seen, ProgrammeBroadcast arriving)
    {
        ProgrammeBroadcast named = arriving.Name.Length > 0 ? arriving : seen;

        return named with
        {
            EndsAt = named.EndsAt ?? seen.EndsAt ?? arriving.EndsAt,
            Summary = named.Summary.Length > 0 ? named.Summary : seen.Summary.Length > 0 ? seen.Summary : arriving.Summary,
            Genres = seen.Genres.Count > 0 ? seen.Genres : arriving.Genres,
            Items = seen.Items.Count > 0 ? seen.Items : arriving.Items,
            Related = seen.Related.Count > 0 ? seen.Related : arriving.Related,
            HasSubtitles = seen.HasSubtitles || arriving.HasSubtitles,
            IsShadow = seen.IsShadow && arriving.IsShadow,
        };
    }

    private static ProgrammeBroadcast? Read(EventInformationTable table, DescribedEvent carried)
    {
        if (carried.EventId is < EventId.MinValue or > EventId.MaxValue)
        {
            return null;
        }

        ShortEventDescription? described = carried.Described;
        ExtendedEventDescription? detailed = carried.Detailed;
        IReadOnlyList<EventGrouping> groupings = carried.Groupings;

        return new ProgrammeBroadcast(
            new ProgrammeId(
                new NetworkId(table.OriginalNetworkId),
                new ServiceId(table.ServiceId),
                new EventId(carried.EventId)),
            new TransportStreamId(table.TransportStreamId),
            carried.StartsAt.UtcDateTime,
            carried.EndsAt?.UtcDateTime,
            described?.Name ?? string.Empty,
            described?.Summary ?? string.Empty,
            IsShadow(described, groupings))
        {
            Genres = [.. carried.Genres.Select(genre => new ProgrammeGenre(genre.Kind, genre.Sort))],
            Items = detailed is null
                ? []
                : [.. detailed.Items.Select(item => new ProgrammeItem(item.Heading, item.Text))],
            Related = [.. Related(table.OriginalNetworkId, table.ServiceId, carried.EventId, groupings)],
            HasSubtitles = carried.DataContents.Any(content => content.CarriesCaptions),
            Source = Source(table),
        };
    }

    private static bool IsShadow(ShortEventDescription? described, IReadOnlyList<EventGrouping> groupings)
        => (described is null || described.Name.Length == 0)
            && groupings.Any(grouping => grouping.Kind is EventGroupKind.Shared);

    private static IEnumerable<RelatedProgramme> Related(
        int networkId,
        int serviceId,
        int eventId,
        IReadOnlyList<EventGrouping> groupings)
    {
        foreach (EventGrouping grouping in groupings)
        {
            if (Relation(grouping.Kind) is not { } kind)
            {
                continue;
            }

            foreach (GroupedEvent carried in grouping.Events)
            {
                if (carried.ServiceId == serviceId && carried.EventId == eventId)
                {
                    continue;
                }

                yield return new RelatedProgramme(networkId, carried.ServiceId, carried.EventId, kind);
            }

            foreach (GroupedEventElsewhere carried in grouping.Elsewhere)
            {
                yield return new RelatedProgramme(carried.NetworkId, carried.ServiceId, carried.EventId, kind);
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
