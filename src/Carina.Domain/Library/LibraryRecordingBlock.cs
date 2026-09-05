using Carina.Domain.Reservations;

namespace Carina.Domain.Library;

public sealed record LibraryRecordingBlock
{
    private LibraryRecordingBlock(BroadcastGroupKey? key, IReadOnlyList<LibraryRecordingSummary> segments)
    {
        Key = key;
        Segments = segments;
    }

    public BroadcastGroupKey? Key { get; }

    public IReadOnlyList<LibraryRecordingSummary> Segments { get; }

    public LibraryRecordingSummary First => Segments[0];

    public static IReadOnlyList<LibraryRecordingBlock> Folded(IEnumerable<LibraryRecordingSummary> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        List<(BroadcastGroupKey? Key, List<LibraryRecordingSummary> Segments)> gathered = [];
        Dictionary<string, int> placeOf = new(StringComparer.Ordinal);

        foreach (LibraryRecordingSummary row in rows)
        {
            if (row.BroadcastGroupKey is { } key && placeOf.TryGetValue(key.Value, out int already))
            {
                gathered[already].Segments.Add(row);

                continue;
            }

            if (row.BroadcastGroupKey is { } opened)
            {
                placeOf[opened.Value] = gathered.Count;
            }

            gathered.Add((row.BroadcastGroupKey, [row]));
        }

        return [.. gathered.Select(block => new LibraryRecordingBlock(block.Key, block.Segments))];
    }
}
