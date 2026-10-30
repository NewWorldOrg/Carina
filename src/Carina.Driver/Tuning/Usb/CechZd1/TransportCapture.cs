using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tuning.Usb.CechZd1;

/// <summary>
/// The path from the demodulator's parallel output to the bulk endpoint: the bridge's capture block writes timed
/// units into a ring in SDRAM, and a program on the first small core hands them to the endpoint.
/// </summary>
public sealed class TransportCapture(BridgeMemory memory)
{
    public const byte Endpoint = 0x81;

    public const int UnitBytes = 192;

    public const int TransferBytes = 1536 * 42;

    public const int TransfersInFlight = 16;

    public const uint FirstCore = 0x1600000;

    public const uint CoreProgramOrigin = 0xA0000;

    public const uint CoreEntry = 0xFFFFC;

    public const uint CoreHeld = 0xFFFFFFFF;

    public const uint CoreRunning = 0xFFFCFFFF;

    public const uint RingStart = 0x00AE4000;

    public const uint RingBytes = 0x00600000;

    public const uint WriterPosition = 0xD02038;

    public const uint ReaderPosition = 0xD0202C;

    private const int ScratchRegister = 12;
    private const int Address = 2;
    private const int Value = 3;
    private const int Filling = 5;
    private const int Sent = 6;
    private const int Event = 7;
    private const int EndpointResetPolls = 100;

    private static readonly IReadOnlyList<(uint Address, uint Value)> CaptureBlock =
    [
        (0xD000C4, 0),
        (0xD00000, 0xFFFFFFFF),
        (0xD00004, 0x001F33FF),
        (0xD00000, 0x30030FFF),
        (0xD00000, 0x00030000),
        (0xD00004, 0x00003305),
        (0xD01040, 0),
        (0xD01064, 0),
        (0xD010A0, 0),
        (0xD010A4, 0),
        (0xD010A8, 0),
        (0xD01020, 0x02000000),
        (0xD01004, 0),
        (0xD01044, 0x00BC0000),
        (0xD01048, 0x47000000),
        (0xD0104C, 0x00FFFFFF),
        (0xD01050, 0),
        (0xD01060, 0),
        (0xD01000, 0x06070000),
        (0xD01080, 0x01000000),
        (0xD01088, 0),
        (0xD0108C, 0xFF),
        (0xD01084, 0),
        (0xD01090, 0x01000000),
        (0xD02028, 0),
        (0xD0202C, 0),
        (0xD02030, 0),
        (0xD02038, 0),
        (0xD02000, 0x03020300),
        (0xD02020, RingStart),
        (0xD02024, RingBytes),
        (0xD00004, 0x00043305),
    ];

    private static readonly IReadOnlyList<(uint Address, uint Value)> Opening =
    [
        (0xD01020, 0),
        (0xD01040, 0x03000000),
        (0xD010A4, 0x13170400),
        (0xD00484, 0x01000000),
        (0xD00488, 0x206C0000),
        (0xD02010, 0x02000000),
    ];

    public static Fr30Program CoreProgram()
    {
        Fr30Assembly program = new(ScratchRegister);

        foreach ((uint at, uint value) in (ReadOnlySpan<(uint, uint)>)
        [
            (0x0000, 0x00000077), (0x0000, 0), (0x0004, 0xFFFFFFFF), (0x0030, 0x004041FF), (0x0050, 0x004041FF),
            (0x0090, 0xFFFFFFFF), (0x00B8, 0xFFFFFFFF), (0x00C8, 0xFFFFFFFF), (0x0010, 0xFFFFFFFF),
            (0x0020, 0xFFFFFFFF), (0x0040, 0), (0x1060, 0x300), (0x1064, 0x300), (0x1068, 0x300), (0x106C, 0x300),
            (0x10B0, 0x300), (0x10B4, 0x300),
        ])
        {
            Store(program, at, value);
        }

        program
            .Label("settle")
            .Load(0x1068, Address)
            .LoadWord(Address, 0)
            .Load(0x300, 1)
            .And(1, 0)
            .CompareSmall(0, 0)
            .Branch(Fr30Condition.NotEqual, "settle");

        Store(program, 0x1018, RingStart);
        Store(program, 0x1028, RingBytes);
        program
            .Load(WriterPosition, Address)
            .LoadWord(Address, 0)
            .Load(0x1048, Address)
            .StoreWord(0, Address)
            .Load(0x1038, Address)
            .StoreWord(0, Address);
        Store(program, 0x1000, 0xFFF00000);

        foreach ((uint at, uint value) in (ReadOnlySpan<(uint, uint)>)
        [
            (0x10010, 0), (0x10030, 0), (0x10034, 0x0800), (0x10038, 0x1000), (0x1003C, 0x1840), (0x10040, 0x1840),
            (0x10080, 0x00B8), (0x10084, 0x00B8), (0x10088, 0x00C0), (0x1008C, 0x00B8), (0x100B0, 0x1880),
            (0x100B4, 0x1900),
        ])
        {
            program.Load(at, Address).Load(value, Value).StoreHalf(Value, Address);
        }

        foreach ((uint at, uint value) in (ReadOnlySpan<(uint, uint)>)
        [
            (0x2020, 0), (0x2024, 0), (0x202C, 0x03000400), (0x2030, 0x05010101), (0x3008, 0), (0x5000, 0x411080C0),
            (0x5008, 0x10000000), (0x5004, 0x01000000), (0x200C, 0x01000000),
        ])
        {
            Store(program, at, value);
        }

        Fill(program);
        program.Load(0, Sent);

        program
            .Label("loop")
            .Load(0x0020, Address)
            .LoadWord(Address, Event)
            .Load(0x20000000, 0)
            .Move(Event, 1)
            .And(0, 1)
            .CompareSmall(0, 1)
            .Branch(Fr30Condition.Equal, "follow")
            .StoreWord(0, Address)
            .Load(0, Filling)
            .Label("follow")
            .Load(WriterPosition, Address)
            .LoadWord(Address, 0)
            .Load(0x1038, Address)
            .StoreWord(0, Address)
            .CompareSmall(0, Filling)
            .Branch(Fr30Condition.NotEqual, "send")
            .Load(0x10068, Address)
            .LoadHalf(Address, 0)
            .Load(0x10058, Address)
            .LoadHalf(Address, 1)
            .Load(0x840, Value)
            .Compare(1, 0)
            .Branch(Fr30Condition.Carry, "behind")
            .Subtract(1, 0)
            .CompareSmall(0, 0)
            .Branch(Fr30Condition.NotEqual, "room")
            .Move(Value, 0)
            .Branch(Fr30Condition.Always, "room")
            .Label("behind")
            .Subtract(1, 0)
            .Add(Value, 0)
            .Label("room")
            .Load(0x470, 1)
            .Compare(1, 0)
            .Branch(Fr30Condition.LowerOrSame, "send");
        Fill(program);

        program
            .Label("send")
            .Load(0x20, 0)
            .And(Event, 0)
            .CompareSmall(0, 0)
            .Branch(Fr30Condition.Equal, "loop")
            .Load(0x2000, Address)
            .LoadWord(Address, 0)
            .Load(7, 1)
            .And(1, 0)
            .CompareSmall(0, 0)
            .Branch(Fr30Condition.NotEqual, "loop");
        Store(program, 0x0020, 8);
        Store(program, 0x200C, 0x01000000);
        Store(program, 0x2004, 0xBF);
        Store(program, 0x2000, 0x0D000041);

        program
            .AddSmall(1, Sent)
            .CompareSmall(10, Sent)
            .Branch(Fr30Condition.NotEqual, "loop")
            .Load(0, Sent)
            .Load(0x1048, Address)
            .LoadWord(Address, 0)
            .CompareSmall(8, 0)
            .Branch(Fr30Condition.NoCarry, "back")
            .Load(RingBytes - 8, 1)
            .Add(1, 0)
            .Branch(Fr30Condition.Always, "tell")
            .Label("back")
            .SubtractSmall(8, 0)
            .Label("tell")
            .Load(ReaderPosition, Address)
            .StoreWord(0, Address)
            .Branch(Fr30Condition.Always, "loop");

        return program.Assemble(CoreProgramOrigin);
    }

    /// <summary>
    /// Readies the capture block, the core and the endpoint, leaving the capture's input and writer stopped.
    /// </summary>
    public void Prepare()
    {
        foreach ((uint at, uint value) in CaptureBlock)
        {
            memory.Write(at, value);
        }

        LoadTheCore(CoreProgram());
        ReadyTheEndpoint();
    }

    public void Start()
    {
        foreach ((uint at, uint value) in Opening)
        {
            memory.Write(at, value);
        }
    }

    public void Stop()
    {
        memory.Write(FirstCore, CoreHeld);
        memory.Write(0xD01040, 0);
        memory.Write(0xD01020, 0x02000000);
        memory.Write(0xD02010, 0x04000000);
    }

    private static void Store(Fr30Assembly program, uint at, uint value) =>
        program.Load(at, Address).Load(value, Value).StoreWord(Value, Address);

    private static void Fill(Fr30Assembly program)
    {
        Store(program, 0x1078, 0x460);
        Store(program, 0x1068, 0x01000000);
        program.Load(1, Filling);
    }

    private void LoadTheCore(Fr30Program program)
    {
        memory.Write(FirstCore, CoreHeld);
        memory.Write(FirstCore + CoreEntry, CoreProgramOrigin);
        uint origin = FirstCore + CoreProgramOrigin;
        uint[] words = [.. program.Words];
        uint[] found = memory.ReadWords(origin, words.Length);

        for (int start = 0; start < words.Length; start += BridgeMemory.MostWordsPerRequest)
        {
            int count = Math.Min(BridgeMemory.MostWordsPerRequest, words.Length - start);

            if (!found.AsSpan(start, count).SequenceEqual(words.AsSpan(start, count)))
            {
                memory.WriteWords(origin + (uint)(4 * start), words.AsSpan(start, count));
            }
        }

        if (!memory.ReadWords(origin, words.Length).SequenceEqual(words))
        {
            throw DvbFailure.Refused(
                $"{memory.Path}: the capture program did not read back from the core as written, so the core is left held."
            );
        }

        memory.Write(FirstCore, CoreRunning);
    }

    private void ReadyTheEndpoint()
    {
        memory.Modify(0xFD8010, 0, 0x000F0000);
        memory.Write(0xFD8200, 1);
        memory.Write(0xFD8220, 1);
        memory.Modify(0xFD8014, 0x000F0000, 0);
        memory.Write(0xFD8240, 0);
        memory.Write(0xFD82A0, 0);
        memory.Write(0xFD8200, 0);
        memory.Write(0xFD8204, 0xB09F0000);
        memory.Write(0xFD8200, 2);
        memory.Write(0xFD8220, 0);
        memory.Write(0xFD8224, 0xB09F2000);
        memory.Write(0xFD8220, 2);
        memory.Write(0xFD8300, 1);
        memory.Write(0xFD8304, 0x00008004);
        memory.Write(0xFDBD08, 0x00FF6001);

        for (int poll = 0; (memory.Read(0xFDBD08) & 1) is not 0; poll++)
        {
            if (poll >= EndpointResetPolls)
            {
                throw DvbFailure.Refused(
                    $"{memory.Path}: the transport stream endpoint did not come out of reset within {EndpointResetPolls} polls."
                );
            }
        }

        memory.Write(0xFDBD08, 0x00FF6000);
        memory.Write(0xFDBC14, 0x00060030);
    }
}
