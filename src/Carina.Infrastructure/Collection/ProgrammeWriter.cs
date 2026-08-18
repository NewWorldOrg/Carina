using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Tables;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Infrastructure.Collection;

public sealed record ProgrammesWritten(int Added, int Updated, int Discarded);

public sealed class ProgrammeWriter(IProgrammeRepository programmes, IAtomicWrite writes, TimeProvider clock)
{
    public async Task<ProgrammesWritten> WriteAsync(
        IReadOnlyList<EventInformationTable> tables,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tables);

        return await writes.AllOrNothingAsync(
            async token =>
            {
                var at = clock.GetUtcNow().UtcDateTime;
                var added = 0;
                var updated = 0;
                var discarded = 0;

                foreach (var table in tables)
                {
                    discarded += table.DiscardedEvents;

                    foreach (var carried in table.Events)
                    {
                        var broadcast = Read(table, carried);

                        if (broadcast is null)
                        {
                            discarded++;

                            continue;
                        }

                        var held = await programmes.FindAsync(broadcast.Id, token);

                        if (held is null)
                        {
                            await programmes.AddAsync(Programme.Discover(broadcast, at), token);
                            added++;

                            continue;
                        }

                        if (held.Absorb(broadcast, at))
                        {
                            await programmes.SaveAsync(held, token);
                            updated++;
                        }
                    }
                }

                return new ProgrammesWritten(added, updated, discarded);
            },
            cancellationToken);
    }

    private static ProgrammeBroadcast? Read(EventInformationTable table, DescribedEvent carried)
    {
        if (carried.EventId is < EventId.MinValue or > EventId.MaxValue)
        {
            return null;
        }

        var described = carried.Described;
        var detailed = carried.Detailed;
        var groupings = carried.Groupings;

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
            Related = [.. Related(groupings)],
            HasSubtitles = carried.DataContents.Any(content => content.CarriesCaptions),
            Source = Source(table),
        };
    }

    private static bool IsShadow(ShortEventDescription? described, IReadOnlyList<EventGrouping> groupings)
        => (described is null || described.Name.Length == 0)
            && groupings.Any(grouping => grouping.Kind is EventGroupKind.Shared);

    private static IEnumerable<RelatedProgramme> Related(IReadOnlyList<EventGrouping> groupings)
    {
        foreach (var grouping in groupings)
        {
            if (Relation(grouping.Kind) is not { } kind)
            {
                continue;
            }

            foreach (var carried in grouping.Events)
            {
                yield return new RelatedProgramme(0, carried.ServiceId, carried.EventId, kind);
            }

            foreach (var carried in grouping.Elsewhere)
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
