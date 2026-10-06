using Microsoft.Extensions.Logging;

namespace Carina.Driver.Descrambling;

/// <summary>
/// Unscrambles a transport stream with the keys a conditional-access card hands back for the ECMs the stream carries.
/// </summary>
public sealed class TransportStreamDescrambler : IDescrambler
{
    public const int PacketLength = 188;

    public const int LongestHold = 8 * 1024 * 1024;

    public const byte EcmTableId = 0x82;

    private const byte SyncByte = 0x47;

    private const int PatPid = 0x0000;

    private const int HeaderLength = 4;

    private const int EvenKey = 2;

    private const int OddKey = 3;

    private readonly ConditionalAccessCard card;

    private readonly ILogger? logger;

    private readonly int holdLimit;

    private readonly ProgramMap map;

    private readonly Lock gate = new();

    private readonly Dictionary<int, SectionCollector> collectors = [];

    private readonly Dictionary<int, EcmState> ecms = [];

    private readonly ByteShelf shelf = new();

    private readonly List<int> waiting = [];

    private byte[] partial = [];

    private bool holding = true;

    private bool disposed;

    public TransportStreamDescrambler(ConditionalAccessCard card, ILogger? logger = null, int holdLimit = LongestHold)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentOutOfRangeException.ThrowIfLessThan(holdLimit, PacketLength);

        this.card = card;
        this.logger = logger;
        this.holdLimit = holdLimit;
        map = new ProgramMap(card.CaSystemId);
    }

    public byte[] Descramble(ReadOnlySpan<byte> stream)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            int shelved = shelf.Length;
            byte[] carried = partial;
            bool wasHolding = holding;

            try
            {
                return Take(stream);
            }
            catch
            {
                shelf.Truncate(shelved);
                waiting.RemoveAll(offset => offset >= shelved);
                partial = carried;
                holding = wasHolding;

                throw;
            }
        }
    }

    /// <summary>
    /// Hands back what was read before the latest call and not yet handed on: the held head and any incomplete packet.
    /// </summary>
    public byte[] WhatItCouldNotRead()
    {
        lock (gate)
        {
            byte[] unread = [.. shelf.Written, .. partial];
            shelf.Clear();
            waiting.Clear();
            partial = [];

            return unread;
        }
    }

    private byte[] Take(ReadOnlySpan<byte> stream)
    {
        byte[] work = [.. partial, .. stream];
        ByteShelf output = new();
        bool released = false;
        int position = NextAlignment(work, 0);

        while (position + PacketLength <= work.Length)
        {
            Span<byte> packet = work.AsSpan(position, PacketLength);
            Unscramble(packet, out bool unkeyed);
            released |= Pass(packet, unkeyed, output);
            position = NextAlignment(work, position + PacketLength);
        }

        partial = work[position..];

        if (released)
        {
            shelf.Clear();
            waiting.Clear();
        }

        return output.ToArray();
    }

    private bool Pass(Span<byte> packet, bool unkeyed, ByteShelf output)
    {
        if (!holding)
        {
            output.Append(packet);

            return false;
        }

        int offset = shelf.Append(packet);
        if (unkeyed)
        {
            waiting.Add(offset);
        }

        if (!HeadIsSettled() && shelf.Length < holdLimit)
        {
            return false;
        }

        if (!HeadIsSettled())
        {
            logger?.LogWarning(
                "The head of the stream was held for {Bytes} bytes without every programme and ECM being read, so it is handed on as it stands.",
                shelf.Length
            );
        }

        holding = false;
        output.Append(shelf.Written);

        return true;
    }

    private static int NextAlignment(byte[] work, int from)
    {
        for (int position = from; position < work.Length; position++)
        {
            if (work[position] is not SyncByte)
            {
                continue;
            }

            int next = position + PacketLength;
            if (next >= work.Length || work[next] is SyncByte)
            {
                return position;
            }
        }

        return Math.Max(from, work.Length - (PacketLength - 1));
    }

    private void Unscramble(Span<byte> packet, out bool unkeyed)
    {
        unkeyed = false;

        if ((packet[1] & 0x80) is not 0)
        {
            return;
        }

        int pid = ((packet[1] & 0x1F) << 8) | packet[2];
        int scrambling = packet[3] >> 6;
        int adaptation = (packet[3] >> 4) & 0x03;
        int start = PayloadStart(packet, adaptation);

        if (start < 0)
        {
            return;
        }

        if (scrambling is 0)
        {
            Read(pid, packet, start, adaptation);

            return;
        }

        if (start == PacketLength)
        {
            packet[3] &= 0x3F;

            return;
        }

        unkeyed = !TryUnscramble(packet, pid, start) && scrambling is EvenKey or OddKey;
    }

    private static int PayloadStart(ReadOnlySpan<byte> packet, int adaptation)
    {
        if (adaptation is 0x01)
        {
            return HeaderLength;
        }

        if (adaptation is 0x00)
        {
            return -1;
        }

        int start = HeaderLength + 1 + packet[HeaderLength];
        if (start > PacketLength || (adaptation is 0x03 && start == PacketLength))
        {
            return -1;
        }

        return adaptation is 0x02 ? PacketLength : start;
    }

    private bool TryUnscramble(Span<byte> packet, int pid, int start)
    {
        int scrambling = packet[3] >> 6;
        if (scrambling is not (EvenKey or OddKey))
        {
            return false;
        }

        ScrambleKeys? keys = KeysFor(pid);
        if (keys is null)
        {
            return false;
        }

        keys.Unscramble(packet[start..], withOddKey: scrambling is OddKey);
        packet[3] &= 0x3F;

        return true;
    }

    private ScrambleKeys? KeysFor(int pid)
    {
        int? ecm = map.EcmFor(pid);

        return ecm is int named && ecms.TryGetValue(named, out EcmState? state) ? state.Keys : null;
    }

    private void Read(int pid, ReadOnlySpan<byte> packet, int start, int adaptation)
    {
        bool section = pid is PatPid || map.IsPmt(pid) || map.IsEcm(pid);
        if (!section || adaptation is 0x02)
        {
            return;
        }

        if (!collectors.TryGetValue(pid, out SectionCollector? collector))
        {
            collector = new SectionCollector();
            collectors[pid] = collector;
        }

        bool unitStart = (packet[1] & 0x40) is not 0;
        collector.Take(packet[start..], unitStart, packet[3] & 0x0F, whole => Whole(pid, whole));
    }

    private void Whole(int pid, ReadOnlySpan<byte> section)
    {
        if (pid is PatPid)
        {
            map.ReadPat(section);
        }
        else if (map.IsPmt(pid))
        {
            map.ReadPmt(section);
        }
        else if (section[0] is EcmTableId && map.IsEcm(pid))
        {
            Ask(pid, PsiSection.Body(section));
        }

        if (holding)
        {
            UnscrambleWhatWaited();
        }
    }

    private void Ask(int pid, ReadOnlySpan<byte> body)
    {
        if (!ecms.TryGetValue(pid, out EcmState? state))
        {
            state = new EcmState();
            ecms[pid] = state;
        }

        if (state.Answered && body.SequenceEqual(state.LastBody))
        {
            return;
        }

        state.LastBody = body.ToArray();
        state.Answered = true;

        if (body.Length is 0 or > ConditionalAccessCard.LongestEcmBody)
        {
            state.Keys = null;

            return;
        }

        EcmAnswer answer = card.Answer(body);
        state.Keys = answer.Keys;

        if (answer.Granted)
        {
            state.LastRefusal = null;

            return;
        }

        if (state.LastRefusal != answer.ReturnCode)
        {
            state.LastRefusal = answer.ReturnCode;
            logger?.LogWarning(
                "The card would not unlock the ECM on PID 0x{Pid:X4} (return code 0x{Code:X4}), so what it covers stays scrambled until the ECM changes.",
                pid,
                answer.ReturnCode
            );
        }
    }

    private void UnscrambleWhatWaited()
    {
        waiting.RemoveAll(offset =>
        {
            Span<byte> packet = shelf.At(offset, PacketLength);
            int pid = ((packet[1] & 0x1F) << 8) | packet[2];
            int adaptation = (packet[3] >> 4) & 0x03;

            return TryUnscramble(packet, pid, PayloadStart(packet, adaptation));
        });
    }

    private bool HeadIsSettled() =>
        map.Complete && map.EcmPids.All(ecm => ecms.TryGetValue(ecm, out EcmState? state) && state.Answered);

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        card.Dispose();
    }

    private sealed class EcmState
    {
        public byte[] LastBody { get; set; } = [];

        public bool Answered { get; set; }

        public ScrambleKeys? Keys { get; set; }

        public ushort? LastRefusal { get; set; }
    }

    private sealed class ByteShelf
    {
        private byte[] bytes = new byte[64 * 1024];

        public int Length { get; private set; }

        public ReadOnlySpan<byte> Written => bytes.AsSpan(0, Length);

        public int Append(ReadOnlySpan<byte> more)
        {
            if (Length + more.Length > bytes.Length)
            {
                Array.Resize(ref bytes, Math.Max(bytes.Length * 2, Length + more.Length));
            }

            int offset = Length;
            more.CopyTo(bytes.AsSpan(Length));
            Length += more.Length;

            return offset;
        }

        public Span<byte> At(int offset, int length) => bytes.AsSpan(offset, length);

        public void Truncate(int length) => Length = Math.Min(Length, length);

        public void Clear() => Length = 0;

        public byte[] ToArray() => Written.ToArray();
    }
}
