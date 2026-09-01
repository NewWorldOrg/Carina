using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveFragmenter
{
    public const int LargestFragment = 32 * 1024 * 1024;

    private const int HeaderLength = 8;

    private const int WideHeaderLength = 16;

    private static readonly IReadOnlyList<LiveFragment> None = [];

    private readonly int largestFragment;

    private readonly List<byte> held = [];

    private int settled;

    private bool headed;

    private bool started;

    private LiveFragmentFault? fault;

    private ReadOnlyMemory<byte>? initialisation;

    public LiveFragmenter(LiveTrack track, int largestFragment = LargestFragment)
    {
        if (!Enum.IsDefined(track))
        {
            throw new ArgumentOutOfRangeException(
                nameof(track),
                track,
                "A fragment belongs to one of the tracks a viewer can be handed.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(largestFragment, WideHeaderLength);

        Track = track;
        this.largestFragment = largestFragment;
    }

    public LiveTrack Track { get; }

    public ReadOnlyMemory<byte>? Initialisation => initialisation;

    public LiveFragmentFault? Fault => fault;

    public LiveFragmenting Read(ReadOnlySpan<byte> bytes)
    {
        if (fault is { } already)
        {
            return new LiveFragmenting(None, already);
        }

        held.AddRange(bytes);

        List<LiveFragment> ready = [];

        Sift(ready);

        return new LiveFragmenting(ready, fault);
    }

    public LiveFragmenting Ended()
    {
        if (fault is { } already)
        {
            return new LiveFragmenting(None, already);
        }

        if (!headed || held.Count > settled || started)
        {
            fault = LiveFragmentFault.StoppedPartWayThrough;

            return new LiveFragmenting(None, fault);
        }

        held.Clear();
        settled = 0;

        return LiveFragmenting.Nothing;
    }

    private static bool Is(ReadOnlySpan<byte> box, ReadOnlySpan<byte> kind) => box[4..8].SequenceEqual(kind);

    private void Sift(List<LiveFragment> ready)
    {
        while (fault is null)
        {
            ReadOnlySpan<byte> ahead = CollectionsMarshal.AsSpan(held)[settled..];

            if (ahead.Length < HeaderLength)
            {
                return;
            }

            uint said = BinaryPrimitives.ReadUInt32BigEndian(ahead);

            if (said is 0)
            {
                fault = LiveFragmentFault.ABoxWithoutAnEnd;

                return;
            }

            if (said is not 1 && said < HeaderLength)
            {
                fault = LiveFragmentFault.ASizeNoBoxCanHave;

                return;
            }

            if (said is 1 && ahead.Length < WideHeaderLength)
            {
                return;
            }

            if (Measured(ahead, said) is not { } length)
            {
                return;
            }

            if (ahead.Length < length)
            {
                return;
            }

            Take(ahead, (int)length, ready);
        }
    }

    private long? Measured(ReadOnlySpan<byte> ahead, uint said)
    {
        if (said is not 1)
        {
            if (settled + said > largestFragment)
            {
                fault = LiveFragmentFault.ABoxTooBigToHold;

                return null;
            }

            return said;
        }

        ulong wide = BinaryPrimitives.ReadUInt64BigEndian(ahead[8..]);

        if (wide < WideHeaderLength)
        {
            fault = LiveFragmentFault.ASizeNoBoxCanHave;

            return null;
        }

        if (wide > (ulong)(largestFragment - settled))
        {
            fault = LiveFragmentFault.ABoxTooBigToHold;

            return null;
        }

        return (long)wide;
    }

    private void Take(ReadOnlySpan<byte> ahead, int length, List<LiveFragment> ready)
    {
        if (!headed)
        {
            if (settled is 0 && !Is(ahead, "ftyp"u8))
            {
                fault = LiveFragmentFault.NotTheContainerItWasAskedFor;

                return;
            }

            if (Is(ahead, "moof"u8))
            {
                fault = LiveFragmentFault.MediaBeforeItsHeader;

                return;
            }

            settled += length;

            if (Is(ahead, "moov"u8))
            {
                headed = true;
                ready.Add(Cut(LiveFragmentKind.Initialisation));
            }

            return;
        }

        bool ends = started && Is(ahead, "mdat"u8);

        started |= Is(ahead, "moof"u8);
        settled += length;

        if (!ends)
        {
            return;
        }

        started = false;
        ready.Add(Cut(LiveFragmentKind.Media));
    }

    private LiveFragment Cut(LiveFragmentKind kind)
    {
        ReadOnlyMemory<byte> bytes = CollectionsMarshal.AsSpan(held)[..settled].ToArray();

        held.RemoveRange(0, settled);
        settled = 0;

        if (kind is LiveFragmentKind.Initialisation)
        {
            initialisation = bytes;
        }

        return new LiveFragment(Track, kind, bytes);
    }
}
