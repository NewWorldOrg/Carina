using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// What putting a recording's matches into the catalog reads: the entries the matches name and every
/// stretch they have, the copies of the programmes of the recordings involved, and how series are
/// linked.
/// </summary>
public sealed record CatalogKnowledge(
    IReadOnlyList<CatalogEntry> Entries,
    IReadOnlyList<CatalogOccurrence> Occurrences,
    IReadOnlyDictionary<RecordingId, ProgrammeCopy> Programmes,
    SeriesLinks Links);

/// <summary>
/// The entries started, the entries already kept that gained stretches and were estimated again, the
/// stretches found, and the stretches already kept that are now taken for another kind on their own.
/// </summary>
public sealed record CatalogChange(
    IReadOnlyList<CatalogEntry> Started,
    IReadOnlyList<CatalogEntry> Reestimated,
    IReadOnlyList<CatalogOccurrence> Found,
    IReadOnlyList<CatalogOccurrence> Overridden);

/// <summary>
/// Puts the matches found in one recording into the catalog. A stretch matched in an entry is a
/// stretch of that entry. A stretch matched in another recording, or elsewhere in itself, joins the
/// entry already heard over the same stretch of the recording, when one is, and otherwise starts an
/// entry whose fingerprint is that stretch of the recording: the matches with entries are taken
/// first, then the longest. A stretch an entry already has in a recording is not added again, and a
/// recording whose programme is not known adds none. An entry started is estimated from its first
/// stretches, and one already kept is estimated again from all of them.
/// </summary>
public static class Catalog
{
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

        foreach (SoundMatch match in matches.OrderBy(match => match.Other is EntrySound ? 0 : 1).ThenByDescending(match => match.Heard.Length).ThenBy(match => match.BitErrorRate))
        {
            absorption.Take(match, sound);
        }

        return absorption.Settled(entries);
    }

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
            List<CatalogEntry> begun = [];
            List<CatalogOccurrence> found = [];

            foreach ((CatalogEntryId id, uint[] frames) in started)
            {
                List<CatalogOccurrence> own = stretches[id];

                if (own.Count >= 2)
                {
                    begun.Add(CatalogEntry.Start(id, frames, Placed(own), known.Links, at));
                    found.AddRange(own);
                }
            }

            List<CatalogEntry> reestimated = [.. grown.Where(id => !started.ContainsKey(id)).Select(id => entries[id])];
            List<CatalogOccurrence> overridden = [];

            foreach (CatalogEntry entry in reestimated)
            {
                List<CatalogOccurrence> own = stretches[entry.Id];
                SegmentKind?[] before = [.. own.Select(occurrence => occurrence.KindOverride)];

                entry.Reestimate(Placed(own), known.Links, at);
                found.AddRange(own.Where(added.Contains));
                overridden.AddRange(own.Where((occurrence, index) => !added.Contains(occurrence) && occurrence.KindOverride != before[index]));
            }

            return new CatalogChange(begun, reestimated, found, overridden);
        }

        private void TakeItself(SoundMatch match, SoundTrack sound)
        {
            CatalogEntryId? claimed = Claimed(match.Heard) ?? Claimed(match.There);
            CatalogEntryId entry = claimed ?? Start(match.Heard, sound);

            Add(entry, heard, match.Heard, claimed is null ? 0 : match.BitErrorRate);
            Add(entry, heard, match.There, match.BitErrorRate);
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

        private List<PlacedOccurrence> Placed(List<CatalogOccurrence> occurrences)
            => [.. occurrences.Select(occurrence => PlacedOccurrence.Of(occurrence, known.Programmes.GetValueOrDefault(occurrence.RecordingId), null))];
    }
}
