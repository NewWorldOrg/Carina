using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// What linking a recording into a series reads of it.
/// </summary>
public sealed record SeriesMember(RecordingId RecordingId, NetworkId NetworkId, ServiceId ServiceId, string? SeriesName, DateTime StartedAt);

/// <summary>
/// Which recordings are of one series. Two stretches of one recording are; so are recordings known to
/// share the sound of an opening, an ending or a programme fixture, whatever their names and stations;
/// and so are two recordings in one slot (<see cref="InOneSlot"/>) whose series names link
/// (<see cref="SeriesNames.Link"/>). Series are what these links join, through one another.
/// </summary>
public sealed class SeriesLinks
{
    public static readonly TimeSpan StartsWithin = TimeSpan.FromMinutes(90);

    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    private readonly Dictionary<RecordingId, int> soundGroups;

    private SeriesLinks(Dictionary<RecordingId, int> soundGroups)
    {
        this.soundGroups = soundGroups;
    }

    public static SeriesLinks None { get; } = new([]);

    /// <summary>
    /// The links that also join each group of recordings known to share a sound that links series
    /// (<see cref="CatalogKinds.LinksSeries"/>). A recording named in two groups joins both.
    /// </summary>
    public static SeriesLinks SharingSound(IEnumerable<IReadOnlyCollection<RecordingId>> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        Dictionary<RecordingId, int> soundGroups = [];
        List<IReadOnlyCollection<RecordingId>> listed = [.. groups];
        int[] parents = [.. Enumerable.Range(0, listed.Count)];

        for (int group = 0; group < listed.Count; group++)
        {
            foreach (RecordingId recording in listed[group])
            {
                if (soundGroups.TryGetValue(recording, out int earlier))
                {
                    parents[Root(parents, group)] = Root(parents, earlier);
                }

                soundGroups[recording] = group;
            }
        }

        return new SeriesLinks(soundGroups.ToDictionary(pair => pair.Key, pair => Root(parents, pair.Value)));
    }

    public bool Link(SeriesMember one, SeriesMember other)
    {
        ArgumentNullException.ThrowIfNull(one);
        ArgumentNullException.ThrowIfNull(other);

        return one.RecordingId.Equals(other.RecordingId) || ShareSound(one, other) || Neighbours(one, other);
    }

    /// <summary>
    /// The members grouped into series, each group in the order the members were handed over, the
    /// groups in the order of their first member.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<SeriesMember>> Group(IReadOnlyList<SeriesMember> members) => Group(members, member => member);

    /// <summary>
    /// The items grouped into series by the member each stands for, in the order of
    /// <see cref="Group(IReadOnlyList{SeriesMember})"/>.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<T>> Group<T>(IReadOnlyList<T> items, Func<T, SeriesMember> member)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(member);

        SeriesMember[] members = [.. items.Select(member)];
        int[] parents = [.. Enumerable.Range(0, members.Length)];

        for (int one = 0; one < members.Length; one++)
        {
            for (int other = one + 1; other < members.Length; other++)
            {
                if (Link(members[one], members[other]))
                {
                    parents[Root(parents, other)] = Root(parents, one);
                }
            }
        }

        return
        [
            .. Enumerable.Range(0, members.Length)
                .GroupBy(at => Root(parents, at))
                .OrderBy(group => group.Min())
                .Select(group => (IReadOnlyList<T>)[.. group.Order().Select(at => items[at])]),
        ];
    }

    /// <summary>
    /// The members among <paramref name="others"/> of the series of <paramref name="judged"/>, joined to it
    /// through one another, in the order they were handed over.
    /// </summary>
    public IReadOnlyList<SeriesMember> SeriesOf(SeriesMember judged, IReadOnlyList<SeriesMember> others)
    {
        ArgumentNullException.ThrowIfNull(judged);
        ArgumentNullException.ThrowIfNull(others);

        IReadOnlyList<SeriesMember> series = Group([judged, .. others.Where(other => !other.RecordingId.Equals(judged.RecordingId))])[0];

        return [.. series.Skip(1)];
    }

    private bool ShareSound(SeriesMember one, SeriesMember other)
        => soundGroups.TryGetValue(one.RecordingId, out int oneGroup)
           && soundGroups.TryGetValue(other.RecordingId, out int otherGroup)
           && oneGroup == otherGroup;

    /// <summary>
    /// Whether two recordings are on the same station and start within <see cref="StartsWithin"/> of each
    /// other in the time of day: the slot a weekly series keeps, whatever it is called.
    /// </summary>
    public static bool InOneSlot(SeriesMember one, SeriesMember other)
    {
        ArgumentNullException.ThrowIfNull(one);
        ArgumentNullException.ThrowIfNull(other);

        return one.NetworkId.Equals(other.NetworkId)
               && one.ServiceId.Equals(other.ServiceId)
               && ApartInTheDay(one.StartedAt, other.StartedAt) <= StartsWithin;
    }

    private static bool Neighbours(SeriesMember one, SeriesMember other)
        => InOneSlot(one, other) && SeriesNames.Link(one.SeriesName, other.SeriesName);

    private static TimeSpan ApartInTheDay(DateTime one, DateTime other)
    {
        TimeSpan apart = (one.TimeOfDay - other.TimeOfDay).Duration();

        return apart > Day / 2 ? Day - apart : apart;
    }

    private static int Root(int[] parents, int at)
    {
        while (parents[at] != at)
        {
            parents[at] = parents[parents[at]];
            at = parents[at];
        }

        return at;
    }
}
