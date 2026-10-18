namespace Carina.Domain.Segments;

/// <summary>
/// The frames of one sound, looked up by fingerprint value: an open-addressed table from each value
/// to the run of frames heard with it. Silence, whose fingerprint has no bit set, is left out, and so
/// is a value heard at more frames than the table is told to keep.
/// </summary>
internal sealed class FingerprintIndex
{
    private const uint Spread = 2654435769;

    private readonly uint[] keys;

    private readonly int[] firsts;

    private readonly int[] counts;

    private readonly int[] places;

    private readonly int shift;

    private FingerprintIndex(uint[] keys, int[] firsts, int[] counts, int[] places, int shift)
    {
        this.keys = keys;
        this.firsts = firsts;
        this.counts = counts;
        this.places = places;
        this.shift = shift;
    }

    public ReadOnlySpan<int> Places => places;

    public static FingerprintIndex Of(ReadOnlySpan<uint> fingerprints, int mostPlaces)
    {
        int heard = fingerprints.Length - fingerprints.Count(0u);
        uint[] values = new uint[heard];
        int[] places = new int[heard];
        int at = 0;

        for (int frame = 0; frame < fingerprints.Length; frame++)
        {
            if (fingerprints[frame] != 0)
            {
                values[at] = fingerprints[frame];
                places[at++] = frame;
            }
        }

        Array.Sort(values, places);

        List<(uint Value, int First, int Count)> runs = Runs(values, mostPlaces);
        int bits = Math.Max(4, 1 + (int)Math.Ceiling(Math.Log2(Math.Max(1, runs.Count))));
        FingerprintIndex index = new(new uint[1 << bits], new int[1 << bits], new int[1 << bits], places, 32 - bits);

        foreach ((uint value, int first, int count) in runs)
        {
            index.Put(value, first, count);
        }

        return index;
    }

    public bool TryFind(uint value, out int first, out int count)
    {
        int mask = keys.Length - 1;

        for (int slot = Slot(value); keys[slot] != 0; slot = (slot + 1) & mask)
        {
            if (keys[slot] == value)
            {
                first = firsts[slot];
                count = counts[slot];

                return true;
            }
        }

        first = 0;
        count = 0;

        return false;
    }

    private static List<(uint Value, int First, int Count)> Runs(uint[] values, int mostPlaces)
    {
        List<(uint Value, int First, int Count)> runs = [];
        int first = 0;

        for (int at = 1; at <= values.Length; at++)
        {
            if (at < values.Length && values[at] == values[first])
            {
                continue;
            }

            if (at - first <= mostPlaces)
            {
                runs.Add((values[first], first, at - first));
            }

            first = at;
        }

        return runs;
    }

    private void Put(uint value, int first, int count)
    {
        int mask = keys.Length - 1;
        int slot = Slot(value);

        while (keys[slot] != 0)
        {
            slot = (slot + 1) & mask;
        }

        keys[slot] = value;
        firsts[slot] = first;
        counts[slot] = count;
    }

    private int Slot(uint value) => (int)((value * Spread) >> shift);
}
