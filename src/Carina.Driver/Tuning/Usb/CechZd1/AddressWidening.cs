using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tuning.Usb.CechZd1;

/// <summary>
/// Diverts the bridge's handling of a memory request through a routine of ours that takes the address bits the
/// handling at power on throws away. Only RAM is written, and the bridge forgets it when power goes.
/// </summary>
public static class AddressWidening
{
    public const uint ReadSideFirst = 0xE9AC0;
    public const uint ReadSideSecond = 0xE9AC4;
    public const uint WriteSideFirst = 0xE9E0C;
    public const uint WriteSideSecond = 0xE9E10;
    public const uint WriteStaging = 0xE9D94;

    public const uint JumpThroughScratch = 0x970C9FA0;
    public const uint StagingWithoutItsGuard = 0x9FA09F8D;

    public const uint FirstPlace = 0xE1000;
    public const uint SecondPlace = 0xE1800;
    public const uint PlaceBytes = 0x800;

    private const uint ReadSideReturn = 0xE9AE6;
    private const uint WriteSideReturn = 0xE9E2E;
    private const uint RemappedFrom = 0x80000;
    private const uint RemappedUntil = 0xA0000;
    private const uint RemapMask = 0x3FFFF;
    private const uint RemappedTo = 0xF00000;
    private const uint HighByteAt = 0x40;
    private const uint AddressFieldAfterHighByte = 0x04;
    private const int ScratchRegister = 12;
    private const int ReadBlock = 8;
    private const int WriteBlock = 10;
    private const int Offset = 0;
    private const int HighByte = 1;
    private const int Address = 4;

    public static readonly IReadOnlyList<(uint Address, uint AtPowerOn)> Patched =
    [
        (ReadSideFirst, 0x9B800000),
        (ReadSideSecond, 0xAA04E40F),
        (WriteSideFirst, 0x9B800000),
        (WriteSideSecond, 0xAA04E40D),
        (WriteStaging, 0xB4279F8D),
    ];

    public static Fr30Program Routine(uint origin)
    {
        Fr30Assembly program = new(ScratchRegister);
        Side(program, "read", ReadBlock, ReadSideReturn, clearsTheHighByte: false);
        program.AlignToWord();
        Side(program, "write", WriteBlock, WriteSideReturn, clearsTheHighByte: true);

        return program.Assemble(origin);
    }

    public static uint LoadOf(uint entry) => ((0x9B0C + (((entry >> 16) & 0xF) * 0x10)) << 16) | (entry & 0xFFFF);

    public static void Widen(BridgeMemory memory)
    {
        uint[] found = [.. Patched.Select(patch => memory.Read(patch.Address))];

        if (Patched.Select((patch, index) => found[index] == patch.AtPowerOn).All(same => same))
        {
            Place(memory, FirstPlace);
            Divert(memory, FirstPlace);
            memory.MarkWidened();

            return;
        }

        if (!TryReadDiversion(found, out uint place, out uint read, out uint write))
        {
            throw Unknown(memory, found);
        }

        if (!Holds(memory, place, read, write))
        {
            uint other = place == FirstPlace ? SecondPlace : FirstPlace;
            Place(memory, other);
            Divert(memory, other);
        }

        memory.MarkWidened();
    }

    private static void Side(Fr30Assembly program, string side, int block, uint back, bool clearsTheHighByte)
    {
        program
            .Label(side)
            .Load(HighByteAt, Offset)
            .Add(block, Offset)
            .LoadByte(Offset, HighByte)
            .CompareSmall(0, HighByte)
            .Branch(Fr30Condition.Equal, side + ".low")
            .ShiftLeft(24, HighByte)
            .Or(HighByte, Address);

        if (clearsTheHighByte)
        {
            program.Load(0, HighByte).StoreByte(HighByte, Offset);
        }

        program
            .AddSmall(AddressFieldAfterHighByte, Offset)
            .StoreWord(Address, Offset)
            .Branch(Fr30Condition.Always, side + ".back")
            .Label(side + ".low")
            .Load(RemappedFrom, HighByte)
            .Compare(HighByte, Address)
            .Branch(Fr30Condition.Carry, side + ".back")
            .Load(RemappedUntil, HighByte)
            .Compare(HighByte, Address)
            .Branch(Fr30Condition.NoCarry, side + ".back")
            .Load(RemapMask, HighByte)
            .And(HighByte, Address)
            .Load(RemappedTo, HighByte)
            .Add(HighByte, Address)
            .AddSmall(AddressFieldAfterHighByte, Offset)
            .StoreWord(Address, Offset)
            .Label(side + ".back")
            .JumpTo(back);
    }

    private static void Place(BridgeMemory memory, uint place)
    {
        IReadOnlyList<uint> words = Routine(place).Words;

        for (int word = 0; word < words.Count; word++)
        {
            memory.Write(place + (uint)(4 * word), words[word]);
        }

        uint[] back = memory.ReadWords(place, words.Count);

        if (!back.SequenceEqual(words))
        {
            throw DvbFailure.Refused(
                $"{memory.Path}: the routine written at 0x{place:X} did not read back as written, so the bridge's request handling is left as it was."
            );
        }
    }

    private static void Divert(BridgeMemory memory, uint place)
    {
        Fr30Program routine = Routine(place);
        uint[] words =
        [
            LoadOf(routine.AddressOf("read")),
            JumpThroughScratch,
            LoadOf(routine.AddressOf("write")),
            JumpThroughScratch,
            StagingWithoutItsGuard,
        ];

        for (int index = 0; index < Patched.Count; index++)
        {
            memory.Write(Patched[index].Address, words[index]);
        }
    }

    private static bool TryReadDiversion(uint[] found, out uint place, out uint read, out uint write)
    {
        place = 0;
        read = 0;
        write = 0;

        if (found[1] != JumpThroughScratch || found[3] != JumpThroughScratch || found[4] != StagingWithoutItsGuard)
        {
            return false;
        }

        if (!TryReadLoad(found[0], out read) || !TryReadLoad(found[2], out write))
        {
            return false;
        }

        place = PlaceOf(read);

        return place is not 0 && PlaceOf(write) == place;
    }

    private static bool TryReadLoad(uint word, out uint entry)
    {
        uint upper = word >> 16;
        entry = (((upper - 0x9B0C) / 0x10) << 16) | (word & 0xFFFF);

        return (upper & 0xFF0F) == 0x9B0C;
    }

    private static uint PlaceOf(uint entry) =>
        entry switch
        {
            >= FirstPlace and < FirstPlace + PlaceBytes => FirstPlace,
            >= SecondPlace and < SecondPlace + PlaceBytes => SecondPlace,
            _ => 0,
        };

    private static bool Holds(BridgeMemory memory, uint place, uint read, uint write)
    {
        Fr30Program routine = Routine(place);

        return read == routine.AddressOf("read")
            && write == routine.AddressOf("write")
            && memory.ReadWords(place, routine.Words.Count).SequenceEqual(routine.Words);
    }

    private static DvbDeviceException Unknown(BridgeMemory memory, uint[] found) =>
        new(
            $"{memory.Path}: the bridge's request handling holds {string.Join(", ", found.Select(word => $"0x{word:X8}"))}, which is neither how it powers on nor a diversion this driver recognises. Unplug the tuner and plug it back in.",
            TuningFailure.DeviceUnusable
        );
}
