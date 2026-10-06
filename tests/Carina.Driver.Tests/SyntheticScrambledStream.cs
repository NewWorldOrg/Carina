using System.Buffers.Binary;

using Carina.Driver.Descrambling;

namespace Carina.Driver.Tests;

internal sealed class SyntheticScrambledStream
{
    public const int PacketLength = 188;

    private readonly Dictionary<int, int> counters = [];

    private readonly List<byte[]> input = [];

    private readonly List<byte[]> output = [];

    public byte[] Input => [.. input.SelectMany(packet => packet)];

    public byte[] Output => [.. output.SelectMany(packet => packet)];

    public int PacketCount => input.Count;

    public SyntheticScrambledStream Pat(params (int Programme, int PmtPid)[] programmes) =>
        PatPart(0, 0, programmes);

    public SyntheticScrambledStream PatPart(int sectionNumber, int lastSectionNumber, params (int Programme, int PmtPid)[] programmes)
    {
        byte[] body = [.. programmes.SelectMany(entry => Be16(entry.Programme).Concat(Be16(0xE000 | entry.PmtPid)))];

        return Section(0x0000, LongSection(ProgramMap.PatTableId, 0x7FE0, body, sectionNumber, lastSectionNumber));
    }

    public SyntheticScrambledStream Pmt(
        int pmtPid,
        int programme,
        int? programmeEcm,
        IReadOnlyList<(int Pid, int? Ecm)> streams,
        int caSystemId = SyntheticCardKeys.CaSystemId
    )
    {
        byte[] programmeInfo = programmeEcm is int ecm ? CaDescriptor(caSystemId, ecm) : [];
        List<byte> body = [.. Be16(0xE000 | 0x1FFF), .. Be16(0xF000 | programmeInfo.Length), .. programmeInfo];

        foreach ((int pid, int? esEcm) in streams)
        {
            byte[] esInfo = esEcm is int named ? CaDescriptor(caSystemId, named) : [];
            body.AddRange([0x02, .. Be16(0xE000 | pid), .. Be16(0xF000 | esInfo.Length), .. esInfo]);
        }

        return Section(pmtPid, LongSection(ProgramMap.PmtTableId, programme, [.. body]));
    }

    public SyntheticScrambledStream Ecm(int pid, byte[] body, bool spoilCrc = false)
    {
        byte[] section = LongSection(TransportStreamDescrambler.EcmTableId, 0x0001, body);
        if (spoilCrc)
        {
            section[^1] ^= 0xFF;
        }

        return Section(pid, section);
    }

    public SyntheticScrambledStream Section(int pid, byte[] section)
    {
        byte[] carried = [0x00, .. section];
        for (int offset = 0; offset < carried.Length; offset += PacketLength - 4)
        {
            int length = Math.Min(PacketLength - 4, carried.Length - offset);
            byte[] payload = new byte[PacketLength - 4];
            Array.Fill(payload, (byte)0xFF);
            carried.AsSpan(offset, length).CopyTo(payload);

            byte[] packet = [.. Header(pid, unitStart: offset is 0, scrambling: 0, adaptation: 1), .. payload];
            input.Add(packet);
            output.Add(packet);
        }

        return this;
    }

    public SyntheticScrambledStream Scrambled(
        int pid,
        byte[] ecmBody,
        bool odd,
        int seed,
        int adaptationLength = -1
    )
    {
        int counter = counters.GetValueOrDefault(pid);
        byte[] clear = ClearPacket(pid, seed, adaptationLength, scrambling: 0);
        counters[pid] = counter;
        byte[] scrambled = ClearPacket(pid, seed, adaptationLength, scrambling: odd ? 3 : 2);
        int start = adaptationLength < 0 ? 4 : 5 + adaptationLength;

        Multi2 key = odd ? SyntheticCardKeys.OddKeyFor(ecmBody) : SyntheticCardKeys.EvenKeyFor(ecmBody);
        key.EncryptPayload(scrambled.AsSpan(start), SyntheticCardKeys.InitialValueAsBlock);

        input.Add(scrambled);
        output.Add(clear);

        return this;
    }

    public SyntheticScrambledStream LeftScrambled(int pid, byte[] ecmBody, bool odd, int seed)
    {
        Scrambled(pid, ecmBody, odd, seed);
        output[^1] = input[^1];

        return this;
    }

    public SyntheticScrambledStream RepeatCounter(int pid)
    {
        counters[pid] = (counters.GetValueOrDefault(pid) + 15) & 15;

        return this;
    }

    public SyntheticScrambledStream Clear(int pid, int seed)
    {
        byte[] packet = ClearPacket(pid, seed, -1, scrambling: 0);
        input.Add(packet);
        output.Add(packet);

        return this;
    }

    public SyntheticScrambledStream Unchanged(byte[] packet)
    {
        input.Add(packet);
        output.Add(packet);

        return this;
    }

    public SyntheticScrambledStream Raw(byte[] inputPacket, byte[] outputPacket)
    {
        input.Add(inputPacket);
        output.Add(outputPacket);

        return this;
    }

    public byte[] Header(int pid, bool unitStart, int scrambling, int adaptation)
    {
        int counter = counters.GetValueOrDefault(pid);
        if (adaptation is 1 or 3)
        {
            counters[pid] = (counter + 1) & 15;
        }

        return
        [
            0x47,
            (byte)((unitStart ? 0x40 : 0x00) | (pid >> 8)),
            (byte)(pid & 0xFF),
            (byte)((scrambling << 6) | (adaptation << 4) | counter),
        ];
    }

    private byte[] ClearPacket(int pid, int seed, int adaptationLength, int scrambling)
    {
        bool adapted = adaptationLength >= 0;
        byte[] header = Header(pid, unitStart: false, scrambling, adaptation: adapted ? 3 : 1);

        byte[] rest = new byte[PacketLength - 4];
        new Random(seed).NextBytes(rest);
        if (adapted)
        {
            rest[0] = (byte)adaptationLength;
            rest.AsSpan(1, adaptationLength).Fill(0xFF);
            if (adaptationLength > 0)
            {
                rest[1] = 0x00;
            }
        }

        return [.. header, .. rest];
    }

    private static byte[] CaDescriptor(int caSystemId, int ecmPid) =>
        [0x09, 0x04, .. Be16(caSystemId), .. Be16(0xE000 | ecmPid)];

    private static byte[] LongSection(byte tableId, int extension, byte[] body, int sectionNumber = 0, int lastSectionNumber = 0)
    {
        int sectionLength = 5 + body.Length + 4;
        byte[] withoutCrc =
        [
            tableId,
            (byte)(0xB0 | (sectionLength >> 8)),
            (byte)(sectionLength & 0xFF),
            .. Be16(extension),
            0xC1,
            (byte)sectionNumber,
            (byte)lastSectionNumber,
            .. body,
        ];
        byte[] crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, PsiSection.Crc(withoutCrc));

        return [.. withoutCrc, .. crc];
    }

    private static byte[] Be16(int value) => [(byte)(value >> 8), (byte)(value & 0xFF)];
}
