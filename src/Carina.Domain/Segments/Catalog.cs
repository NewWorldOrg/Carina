using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// What putting a recording's matches into the catalog reads: the entries the matches name and every
/// stretch they have, the copies of the programmes of the recordings involved, how series are linked, and
/// the stretches heard in the recordings where an entry may carry on an opening or an ending
/// (<see cref="CatalogKinds.CarryOn"/>), none when it is not said.
/// </summary>
public sealed record CatalogKnowledge(
    IReadOnlyList<CatalogEntry> Entries,
    IReadOnlyList<CatalogOccurrence> Occurrences,
    IReadOnlyDictionary<RecordingId, ProgrammeCopy> Programmes,
    SeriesLinks Links,
    CatalogSurroundings? Surroundings = null)
{
    public CatalogSurroundings Around => Surroundings ?? CatalogSurroundings.None;

    /// <summary>
    /// A stretch beside where its programme starts and ends.
    /// </summary>
    public PlacedOccurrence Placed(CatalogOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        return PlacedOccurrence.Of(occurrence, Programmes.GetValueOrDefault(occurrence.RecordingId));
    }
}

/// <summary>
/// The entries started, the entries already kept that were estimated again, the stretches found, the
/// stretches already kept that are now taken for another kind on their own, the entries whose links
/// between series changed (<see cref="CatalogKinds.LinksSeries(CatalogKind)"/>): every other entry heard in
/// one of their recordings was estimated with links that no longer hold; and the entries whose kind or confidence is
/// not what it was, started as a kind or estimated again as another kind or as surer or less sure, which changes what
/// the catalog hears in every recording they are heard in.
/// </summary>
public sealed record CatalogChange(
    IReadOnlyList<CatalogEntry> Started,
    IReadOnlyList<CatalogEntry> Reestimated,
    IReadOnlyList<CatalogOccurrence> Found,
    IReadOnlyList<CatalogOccurrence> Overridden,
    IReadOnlyList<CatalogEntryId> Relinking,
    IReadOnlyList<CatalogEntryId> Recast);

/// <summary>
/// Puts the matches found in one recording into the catalog. A stretch matched in an entry is a
/// stretch of that entry. A stretch matched in another recording, or elsewhere in itself, joins the
/// entry already heard over the same stretch of the recording, when one is, and otherwise starts an
/// entry whose fingerprint is that stretch of the recording: the matches with entries are taken
/// first, then those within the recording itself, which join both of their stretches, then those with
/// other recordings, each kind the longest first. A stretch an entry already has in a recording is not added again, and a
/// recording whose programme is not known adds none. An entry started is estimated from its first
/// stretches, and one already kept is estimated again from all of them, each beside the stretches heard around
/// it as the others started or estimated again now take theirs. The links between series change with an entry started as a kind
/// that links them, and with an entry estimated again that links them before or after, since its stretches grew.
/// </summary>
public static class Catalog
{
    /// <summary>
    /// Estimates a kept entry again from every stretch it has, as the estimation now stands, and hands it
    /// back with the stretches whose own kind changed, and as changing the links between series when it began
    /// or stopped linking them.
    /// </summary>
    public static CatalogChange Reestimate(CatalogEntry entry, CatalogKnowledge known, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(known);

        CatalogOccurrence[] own = [.. known.Occurrences.Where(occurrence => occurrence.EntryId.Equals(entry.Id))];
        PlacedOccurrence[] placed = [.. own.Select(known.Placed)];
        SegmentKind?[] before = [.. own.Select(occurrence => occurrence.KindOverride)];
        bool linked = CatalogKinds.LinksSeries(entry.Kind);
        (CatalogKind, double?) was = (entry.Kind, entry.Confidence);

        entry.Take(placed, CatalogKinds.Estimate(entry.Length, placed, known.Links, known.Around), at);

        return new CatalogChange(
            [],
            [entry],
            [],
            [.. own.Where((occurrence, index) => occurrence.KindOverride != before[index])],
            linked != CatalogKinds.LinksSeries(entry.Kind) ? [entry.Id] : [],
            was != (entry.Kind, entry.Confidence) ? [entry.Id] : []);
    }

    public static CatalogChange Absorb(
        RecordingId heard,
        SoundTrack sound,
        IReadOnlyList<SoundMatch> matches,
        CatalogKnowledge known,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(heard);
        ArgumentNullException.ThrowIfNull(sound);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(known);

        Dictionary<CatalogEntryId, CatalogEntry> entries = known.Entries.ToDictionary(entry => entry.Id);

        if (matches.Select(match => match.Other).OfType<EntrySound>().FirstOrDefault(named => !entries.ContainsKey(named.Entry)) is not null)
        {
            throw new ArgumentException("Every entry a match names is handed over with what is known.", nameof(known));
        }

        Absorption absorption = new(heard, known, at);

        foreach (SoundMatch match in matches.OrderBy(match => Rank(match, heard)).ThenByDescending(match => match.Heard.Length).ThenBy(match => match.BitErrorRate))
        {
            absorption.Take(match, sound);
        }

        return absorption.Settled(entries);
    }

    private static int Rank(SoundMatch match, RecordingId heard) => match.Other switch
    {
        EntrySound => 0,
        RecordedSound recorded when recorded.Recording.Equals(heard) => 1,
        _ => 2,
    };

    private sealed class Absorption(RecordingId heard, CatalogKnowledge known, DateTime at)
    {
        private readonly List<(SoundSpan Span, CatalogEntryId Entry)> claims = [];

        private readonly Dictionary<CatalogEntryId, List<CatalogOccurrence>> stretches = known.Occurrences
            .GroupBy(occurrence => occurrence.EntryId)
            .ToDictionary(group => group.Key, group => group.ToList());

        private readonly Dictionary<CatalogEntryId, uint[]> started = [];

        private readonly HashSet<CatalogEntryId> grown = [];

        private readonly HashSet<CatalogOccurrence> added = [];

        public void Take(SoundMatch match, SoundTrack sound)
        {
            switch (match.Other)
            {
                case EntrySound named:
                    Add(named.Entry, heard, match.Heard, match.BitErrorRate);
                    claims.Add((match.Heard, named.Entry));
                    break;
                case RecordedSound recorded when recorded.Recording.Equals(heard):
                    TakeItself(match, sound);
                    break;
                case RecordedSound recorded:
                    CatalogEntryId entry = Claimed(match.Heard) ?? Start(match.Heard, sound);
                    Add(entry, recorded.Recording, match.There, match.BitErrorRate);
                    break;
            }
        }

        public CatalogChange Settled(Dictionary<CatalogEntryId, CatalogEntry> entries)
        {
            CatalogEntryId[] starting = [.. started.Keys.Where(id => stretches[id].Count >= CatalogEntry.FewestOccurrences)];
            List<CatalogEntry> reestimated = [.. grown.Where(id => !started.ContainsKey(id)).Select(id => entries[id])];
            Dictionary<CatalogEntryId, TimeSpan> lengths = new(
            [
                .. starting.Select(id => KeyValuePair.Create(id, SegmentMatcher.Frame * started[id].Length)),
                .. reestimated.Select(entry => KeyValuePair.Create(entry.Id, entry.Length)),
            ]);
            Dictionary<CatalogEntryId, List<PlacedOccurrence>> placed = lengths.Keys.ToDictionary(id => id, id => Placed(stretches[id]));
            Dictionary<CatalogEntryId, CatalogPattern> own = lengths.Keys.ToDictionary(id => id, id => CatalogKinds.Estimate(lengths[id], placed[id], known.Links));
            CatalogSurroundings around = known.Around.Replacing(lengths.Keys, own.SelectMany(pair => CatalogKinds.Stretches(placed[pair.Key], pair.Value)));
            List<CatalogEntry> begun = [];
            List<CatalogOccurrence> found = [];

            foreach (CatalogEntryId id in starting)
            {
                begun.Add(CatalogEntry.Start(id, started[id], placed[id], Carried(id), at));
                found.AddRange(stretches[id]);
            }

            List<CatalogOccurrence> overridden = [];
            List<CatalogEntryId> relinking = [.. begun.Where(entry => CatalogKinds.LinksSeries(entry.Kind)).Select(entry => entry.Id)];
            List<CatalogEntryId> recast = [.. begun.Where(entry => entry.Kind is not CatalogKind.Unknown).Select(entry => entry.Id)];

            foreach (CatalogEntry entry in reestimated)
            {
                List<CatalogOccurrence> kept = stretches[entry.Id];
                SegmentKind?[] before = [.. kept.Select(occurrence => occurrence.KindOverride)];
                bool linked = CatalogKinds.LinksSeries(entry.Kind);
                (CatalogKind, double?) was = (entry.Kind, entry.Confidence);

                entry.Take(placed[entry.Id], Carried(entry.Id), at);

                if (was != (entry.Kind, entry.Confidence))
                {
                    recast.Add(entry.Id);
                }
                found.AddRange(kept.Where(added.Contains));
                overridden.AddRange(kept.Where((occurrence, index) => !added.Contains(occurrence) && occurrence.KindOverride != before[index]));

                if (linked || CatalogKinds.LinksSeries(entry.Kind))
                {
                    relinking.Add(entry.Id);
                }
            }

            return new CatalogChange(begun, reestimated, found, overridden, relinking, recast);

            CatalogPattern Carried(CatalogEntryId id) => CatalogKinds.CarryOn(lengths[id], placed[id], own[id], around);
        }

        private void TakeItself(SoundMatch match, SoundTrack sound)
        {
            CatalogEntryId? claimed = Claimed(match.Heard) ?? Claimed(match.There);
            CatalogEntryId entry = claimed ?? Start(match.Heard, sound);

            Add(entry, heard, match.Heard, claimed is null ? 0 : match.BitErrorRate);
            Add(entry, heard, match.There, match.BitErrorRate);
            claims.Add((match.Heard, entry));
            claims.Add((match.There, entry));
        }

        private CatalogEntryId? Claimed(SoundSpan span) => claims.Where(claim => claim.Span.MostlyShares(span)).Select(claim => claim.Entry).FirstOrDefault();

        private CatalogEntryId Start(SoundSpan span, SoundTrack sound)
        {
            CatalogEntryId entry = CatalogEntryId.New();

            started[entry] = sound.Slice(span);
            stretches[entry] = [];
            claims.Add((span, entry));
            Add(entry, heard, span, 0);

            return entry;
        }

        private void Add(CatalogEntryId entry, RecordingId recording, SoundSpan span, double bitErrorRate)
        {
            List<CatalogOccurrence> own = stretches.TryGetValue(entry, out List<CatalogOccurrence>? kept) ? kept : stretches[entry] = [];

            if (!known.Programmes.TryGetValue(recording, out ProgrammeCopy? programme)
                || own.Any(occurrence => occurrence.RecordingId.Equals(recording) && occurrence.Span.MostlyShares(span)))
            {
                return;
            }

            CatalogOccurrence occurrence = CatalogOccurrence.Found(entry, recording, span, programme, bitErrorRate, at);

            own.Add(occurrence);
            added.Add(occurrence);
            grown.Add(entry);
        }

        private List<PlacedOccurrence> Placed(List<CatalogOccurrence> occurrences) => [.. occurrences.Select(known.Placed)];
    }
}
