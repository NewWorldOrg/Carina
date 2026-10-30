using Carina.Driver.Tuning.Dvb;
using Carina.Driver.Tuning.Usb;
using Carina.Driver.Tuning.Usb.CechZd1;

namespace Carina.Driver.Tests;

public sealed class UsbdevfsLayoutTests
{
    [Fact]
    public void TheRequestsCarryTheirDirectionAndTheSizeOfWhatTheyPointAt()
    {
        Assert.Equal(0xC0185500u, UsbdevfsLayout.Control);
        Assert.Equal(0x8038550Au, UsbdevfsLayout.SubmitUrb);
        Assert.Equal(0x0000550Bu, UsbdevfsLayout.DiscardUrb);
        Assert.Equal(0x4008550Du, UsbdevfsLayout.ReapUrbWithoutWaiting);
        Assert.Equal(0x8004550Fu, UsbdevfsLayout.ClaimInterface);
        Assert.Equal(0x80045510u, UsbdevfsLayout.ReleaseInterface);
    }
}

public sealed class Fr30AssemblyTests
{
    [Fact]
    public void ALoadOfTwentyBitsCarriesTheTopFourBitsInTheFirstHalf()
    {
        Fr30Program program = new Fr30Assembly(12).LoadTwenty(0xE1000, 12).Jump(12).Assemble(0);

        Assert.Equal([0x9BEC1000u, 0x970C9FA0u], program.Words);
    }

    [Fact]
    public void TheDiversionLoadNamesItsEntry()
    {
        Assert.Equal(0x9BEC1000u, AddressWidening.LoadOf(0xE1000));
        Assert.Equal(0x9BEC103Cu, AddressWidening.LoadOf(0xE103C));
    }

    [Fact]
    public void ConstantsTakeTheShortestLoadThatHoldsThem()
    {
        Fr30Program program = new Fr30Assembly(12)
            .Load(0x40, 0)
            .Load(0x80000, 1)
            .Load(0xF00000, 1)
            .Assemble(0);

        Assert.Equal([0xC4009B81u, 0x00009F81u, 0x00F00000u], program.Words);
    }

    [Fact]
    public void ShiftsOfSixteenOrMoreUseTheSecondForm()
    {
        Fr30Program program = new Fr30Assembly(12).ShiftLeft(24, 1).ShiftLeft(4, 2).Assemble(0);

        Assert.Equal([0xB581B442u], program.Words);
    }

    [Fact]
    public void ABranchCountsHalvesFromTheInstructionAfterIt()
    {
        Fr30Program program = new Fr30Assembly(12)
            .Label("top")
            .NoOperation()
            .Branch(Fr30Condition.NotEqual, "top")
            .Assemble(0);

        Assert.Equal([0x9FA0E3FEu], program.Words);
    }

    [Fact]
    public void ABranchOutOfReachStepsOverAFarJump()
    {
        Fr30Assembly assembly = new Fr30Assembly(12).Branch(Fr30Condition.Equal, "far");

        for (int filler = 0; filler < 200; filler++)
        {
            assembly.NoOperation();
        }

        Fr30Program program = assembly.Label("far").NoOperation().Assemble(0xA0000);
        uint far = program.AddressOf("far");

        Assert.Equal(0xE3039B0Cu | ((far >> 16) & 0xF) << 4, program.Words[0]);
        Assert.Equal((far & 0xFFFF) << 16 | 0x970C, program.Words[1]);
    }
}

public sealed class BridgeMemoryTests
{
    [Fact]
    public void TheAddressRidesInTheValueAndIndexOfTheVendorRequest()
    {
        UsbSetup setup = BridgeMemory.SetupFor(BridgeMemory.ReadRequestType, 0x12345678);

        Assert.Equal(new UsbSetup(0xC0, 0xBC, 0x3412, 0x5678), setup);
    }

    [Fact]
    public void WordsTravelBigEndian()
    {
        var bridge = new FakeBridge();
        bridge.Memory[0x100] = 0x01020304;
        var memory = new BridgeMemory(bridge);

        Assert.Equal(0x01020304u, memory.Read(0x100));
    }

    [Fact]
    public void BeforeWideningEveryWordIsItsOwnRequest()
    {
        var bridge = new FakeBridge();
        var memory = new BridgeMemory(bridge);

        memory.WriteWords(0x1000, [1, 2, 3]);

        Assert.Equal(3, bridge.Requests.Count);
        Assert.Equal([1u, 2u, 3u], [bridge.Word(0x1000), bridge.Word(0x1004), bridge.Word(0x1008)]);
    }

    [Fact]
    public void AfterWideningSixteenWordsGoInOneRequest()
    {
        var bridge = new FakeBridge();
        var memory = new BridgeMemory(bridge);
        AddressWidening.Widen(memory);
        bridge.Requests.Clear();

        memory.WriteWords(0x1600000, [.. Enumerable.Range(0, 20).Select(word => (uint)word)]);

        Assert.Equal(2, bridge.Requests.Count);
        Assert.Equal(19u, bridge.Word(0x1600000 + (19 * 4)));
    }

    [Fact]
    public void AnAddressBeyondTwentyFourBitsIsRefusedBeforeWidening()
    {
        var memory = new BridgeMemory(new FakeBridge());

        Assert.Throws<InvalidOperationException>(() => memory.Read(0x1600000));
    }

    [Fact]
    public void ASixteenBitRegisterKeepsTheLowerHalfAsRead()
    {
        var bridge = new FakeBridge();
        bridge.Memory[0xF1010C] = 0x1234ABCD;
        var memory = new BridgeMemory(bridge);

        memory.WriteHalf(0xF1010C, 0x8201);

        Assert.Equal(0x8201ABCDu, bridge.Word(0xF1010C));
    }
}

public sealed class AddressWideningTests
{
    [Fact]
    public void FromPowerOnTheRoutineGoesInTheFirstPlaceAndTheFiveWordsPointAtIt()
    {
        var bridge = new FakeBridge();
        var memory = new BridgeMemory(bridge);

        AddressWidening.Widen(memory);

        Fr30Program routine = AddressWidening.Routine(AddressWidening.FirstPlace);
        Assert.True(memory.Widened);
        Assert.Equal(0x9BEC1000u, bridge.Word(AddressWidening.ReadSideFirst));
        Assert.Equal(AddressWidening.JumpThroughScratch, bridge.Word(AddressWidening.ReadSideSecond));
        Assert.Equal(AddressWidening.LoadOf(routine.AddressOf("write")), bridge.Word(AddressWidening.WriteSideFirst));
        Assert.Equal(AddressWidening.JumpThroughScratch, bridge.Word(AddressWidening.WriteSideSecond));
        Assert.Equal(AddressWidening.StagingWithoutItsGuard, bridge.Word(AddressWidening.WriteStaging));
        Assert.Equal(0u, routine.AddressOf("write") % 4);
        Assert.Equal(routine.Words[0], bridge.Word(AddressWidening.FirstPlace));
    }

    [Fact]
    public void TheFiveWordsAreWrittenInTheOrderOfTheTable()
    {
        var bridge = new FakeBridge();

        AddressWidening.Widen(new BridgeMemory(bridge));

        uint[] patched = [.. AddressWidening.Patched.Select(patch => patch.Address)];
        Assert.Equal(patched, bridge.Writes.Select(write => write.Address).Where(patched.Contains));
    }

    [Fact]
    public void WideningTwiceWritesNothingTheSecondTime()
    {
        var bridge = new FakeBridge();
        AddressWidening.Widen(new BridgeMemory(bridge));
        bridge.Writes.Clear();

        var reopened = new BridgeMemory(bridge);
        AddressWidening.Widen(reopened);

        Assert.Empty(bridge.Writes);
        Assert.True(reopened.Widened);
    }

    [Fact]
    public void ARoutineThatIsNotOursIsReplacedFromTheOtherPlace()
    {
        var bridge = new FakeBridge();
        AddressWidening.Widen(new BridgeMemory(bridge));
        bridge.Memory[AddressWidening.FirstPlace + 8] ^= 0xFFFF;

        AddressWidening.Widen(new BridgeMemory(bridge));

        Assert.Equal(AddressWidening.LoadOf(AddressWidening.SecondPlace), bridge.Word(AddressWidening.ReadSideFirst));
        Assert.Equal(
            AddressWidening.Routine(AddressWidening.SecondPlace).Words[0],
            bridge.Word(AddressWidening.SecondPlace)
        );
    }

    [Fact]
    public void WordsItDoesNotRecogniseAskForTheTunerToBeReplugged()
    {
        var bridge = new FakeBridge();
        bridge.Memory[AddressWidening.ReadSideFirst] = 0x12345678;

        DvbDeviceException refused = UsbTestSupport.Refusal(() => AddressWidening.Widen(new BridgeMemory(bridge)));

        Assert.Equal(TuningFailure.DeviceUnusable, refused.Failure);
        Assert.Contains("plug it back in", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRoutineFitsItsPlace()
    {
        Assert.True(AddressWidening.Routine(AddressWidening.FirstPlace).Words.Count * 4 <= AddressWidening.PlaceBytes);
    }
}

public sealed class BridgeStartupTests
{
    [Fact]
    public void ABridgeSomethingElseHasSetUpIsLeftAlone()
    {
        var bridge = new FakeBridge();
        bridge.Memory[0xF02008] = 0x00010000;
        (DevicePacing pacing, _, _) = UsbTestSupport.Pacing();

        DvbDeviceException refused = UsbTestSupport.Refusal(() => BridgeStartup.Start(new BridgeMemory(bridge), pacing));

        Assert.Equal(TuningFailure.DeviceUnusable, refused.Failure);
        Assert.Contains("plug it back in", refused.Message, StringComparison.Ordinal);
        Assert.Empty(bridge.Writes);
    }

    [Fact]
    public void TheDemodulatorIsPoweredHeldInResetAndThenReleased()
    {
        var bridge = new FakeBridge();
        (DevicePacing pacing, _, List<TimeSpan> slept) = UsbTestSupport.Pacing();

        BridgeStartup.Start(new BridgeMemory(bridge), pacing);

        Assert.Equal([0x00400000u, 0x00410000u], bridge.WritesAt(BridgeStartup.GpioValue));
        Assert.Equal([0x00410000u], bridge.WritesAt(BridgeStartup.GpioDirection));
        Assert.Equal([TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10)], slept);
    }

    [Fact]
    public void EveryInterruptLevelIsRaisedToTheOneThatIsNeverTaken()
    {
        var bridge = new FakeBridge();
        bridge.Memory[0x440] = 0x01020304;
        bridge.Memory[0x444] = 0x1F1F1F1F;
        (DevicePacing pacing, _, _) = UsbTestSupport.Pacing();

        BridgeStartup.Start(new BridgeMemory(bridge), pacing);

        Assert.Equal([0x1F1F1F1Fu], bridge.WritesAt(0x440));
        Assert.Empty(bridge.WritesAt(0x444));
    }
}

public sealed class BridgeI2cTests
{
    [Fact]
    public void ADemodulatorRegisterIsWrittenAsAddressRegisterAndValue()
    {
        var chip = new FakeChip(2);
        var bridge = new FakeBridge();
        bridge.Chips[Dib8000Bus.Address] = chip;

        new Dib8000Bus(new BridgeI2c(new BridgeMemory(bridge))).Write(1287, 0x0003);

        Assert.Equal([(1287, (ushort)0x0003)], chip.Written);
        Assert.Equal(
            [0x12u, 0x05u, 0x07u, 0x00u, 0x03u],
            bridge.WritesAt(BridgeI2c.Data)
        );
    }

    [Fact]
    public void ATunerRegisterIsReadWithARepeatedStart()
    {
        var chip = new FakeChip(1);
        chip.Answer(0x1A, 0x0223);
        var bridge = new FakeBridge();
        bridge.Chips[Dib0090Bus.Address] = chip;

        ushort read = new Dib0090Bus(new BridgeI2c(new BridgeMemory(bridge))).Read(0x1A);

        Assert.Equal(0x0223, read);
        Assert.Equal(
            [BridgeI2c.SendAfterStart, BridgeI2c.SendAfterStart, BridgeI2c.SendAfterRepeatedStart,
                BridgeI2c.ReceiveAndAcknowledge, BridgeI2c.SendAfterStart, BridgeI2c.Idle],
            bridge.WritesAt(BridgeI2c.Control)
        );
    }

    [Fact]
    public void ADeviceThatDoesNotAnswerLeavesTheBusReleased()
    {
        var bridge = new FakeBridge();

        DvbDeviceException refused = UsbTestSupport.Refusal(
            () => new Dib8000Bus(new BridgeI2c(new BridgeMemory(bridge))).Write(1, 2)
        );

        Assert.Equal(TuningFailure.DeviceUnusable, refused.Failure);
        Assert.Equal(BridgeI2c.Idle, bridge.WritesAt(BridgeI2c.Control)[^1]);
    }
}
