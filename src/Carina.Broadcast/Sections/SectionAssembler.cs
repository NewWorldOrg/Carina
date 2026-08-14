namespace Carina.Broadcast.Sections;

public sealed class SectionAssembler
{
    public const byte StuffingByte = 0xFF;

    private static readonly IReadOnlyList<SectionRead> Nothing = [];

    private readonly byte[] pending = new byte[Section.LengthPrefixSize + Section.MaximumDeclaredLength];

    private int pendingCount;
    private int pendingTotal = -1;
    private int lastContinuityCounter = -1;
    private bool awaitingStart = true;

    public SectionAssembler(int pid)
    {
        if (pid is < 0 or > TransportPacket.NullPacketPid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pid),
                pid,
                $"A pid is 0 to {TransportPacket.NullPacketPid}.");
        }

        Pid = pid;
    }

    public int Pid { get; }

    public IReadOnlyList<SectionRead> Push(ReadOnlySpan<byte> packet)
    {
        if (!TransportPacket.TryRead(packet, out var read))
        {
            Abandon();

            return [Rejected(SectionDefect.PacketNotSynchronised)];
        }

        if (read.Pid != Pid)
        {
            return Nothing;
        }

        if (read.TransportError)
        {
            Abandon();

            return [Rejected(SectionDefect.TransportError)];
        }

        if (read.IsScrambled)
        {
            Abandon();

            return [Rejected(SectionDefect.Scrambled)];
        }

        if (!read.HasPayload)
        {
            return Nothing;
        }

        var outcomes = new List<SectionRead>();

        if (lastContinuityCounter >= 0)
        {
            if (read.ContinuityCounter == lastContinuityCounter)
            {
                return Nothing;
            }

            if (read.ContinuityCounter != ((lastContinuityCounter + 1) & 0x0F))
            {
                Abandon();
                outcomes.Add(Rejected(SectionDefect.ContinuityBroken));

                if (!read.PayloadUnitStart)
                {
                    lastContinuityCounter = read.ContinuityCounter;

                    return outcomes;
                }
            }
        }

        lastContinuityCounter = read.ContinuityCounter;

        if (read.PayloadUnitStart)
        {
            StartAndContinue(read.Payload, outcomes);
        }
        else if (!awaitingStart && pendingCount > 0)
        {
            Feed(read.Payload, outcomes);
        }

        return outcomes;
    }

    public SectionRead? Flush()
    {
        if (pendingCount == 0)
        {
            Abandon();

            return null;
        }

        Abandon();

        return Rejected(SectionDefect.Truncated);
    }

    public void Reset()
    {
        Abandon();
        lastContinuityCounter = -1;
    }

    private void StartAndContinue(ReadOnlySpan<byte> payload, List<SectionRead> outcomes)
    {
        if (payload.IsEmpty)
        {
            return;
        }

        var pointer = payload[0];
        var rest = payload[1..];

        if (pointer > rest.Length)
        {
            Abandon();
            outcomes.Add(Rejected(SectionDefect.PointerOutOfRange));

            return;
        }

        if (pendingCount > 0)
        {
            Feed(rest[..pointer], outcomes);

            if (pendingCount > 0)
            {
                Abandon();
                outcomes.Add(Rejected(SectionDefect.Truncated));
            }
        }

        awaitingStart = false;
        Feed(rest[pointer..], outcomes);
    }

    private void Feed(ReadOnlySpan<byte> bytes, List<SectionRead> outcomes)
    {
        var at = 0;

        while (at < bytes.Length)
        {
            if (pendingCount == 0 && bytes[at] == StuffingByte)
            {
                return;
            }

            var wanted = pendingTotal < 0
                ? Section.LengthPrefixSize - pendingCount
                : pendingTotal - pendingCount;
            var taken = Math.Min(wanted, bytes.Length - at);

            bytes.Slice(at, taken).CopyTo(pending.AsSpan(pendingCount));
            pendingCount += taken;
            at += taken;

            if (pendingTotal < 0 && pendingCount == Section.LengthPrefixSize)
            {
                var declared = ((pending[1] & 0x0F) << 8) | pending[2];
                var longForm = (pending[1] & 0x80) != 0;

                if (declared > Section.MaximumDeclaredLength
                    || (longForm && declared < Section.MinimumLongFormLength))
                {
                    Abandon();
                    outcomes.Add(Rejected(SectionDefect.LengthOutOfRange));

                    return;
                }

                pendingTotal = Section.LengthPrefixSize + declared;
            }

            if (pendingTotal >= 0 && pendingCount == pendingTotal)
            {
                outcomes.Add(Complete());
                pendingCount = 0;
                pendingTotal = -1;
            }
        }
    }

    private SectionRead Complete()
    {
        var raw = pending.AsSpan(0, pendingTotal).ToArray();

        if ((raw[1] & 0x80) == 0)
        {
            return Rejected(SectionDefect.ShortFormSection);
        }

        return Crc32Mpeg.Verifies(raw)
            ? new SectionRead.Assembled(Pid, Section.Over(raw))
            : Rejected(SectionDefect.ChecksumMismatch);
    }

    private void Abandon()
    {
        pendingCount = 0;
        pendingTotal = -1;
        awaitingStart = true;
    }

    private SectionRead Rejected(SectionDefect defect) => new SectionRead.Rejected(Pid, defect);
}
