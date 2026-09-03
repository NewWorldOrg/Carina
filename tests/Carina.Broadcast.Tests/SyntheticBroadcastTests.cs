using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests;

public sealed class SyntheticBroadcastTests
{
    [Fact]
    public void TheSideInformationCarriesAnAssociationAMapAndTheCaptionStreams()
    {
        byte[] bytes = SyntheticBroadcast.AsMeasured().SideInformation();
        List<byte[]> packets = Packets(bytes);

        Assert.All(packets, packet => Assert.Equal(TransportStreamWriter.SyncByte, packet[0]));

        byte[] map = Section(packets.First(packet => PidOf(packet) == SyntheticBroadcast.PmtPid));
        byte[] association = Section(packets.First(packet => PidOf(packet) == PatWriter.Pid));

        Assert.Equal(PmtWriter.TableId, map[0]);
        Assert.Equal(PatWriter.TableId, association[0]);
        Assert.Equal(0u, ReferenceCrc32.Compute(map));
        Assert.Equal(0u, ReferenceCrc32.Compute(association));
        Assert.Equal(SyntheticBroadcast.SomeProgramNumber, (map[3] << 8) | map[4]);
        Assert.Contains(
            PmtWriter.Stream(
                PmtWriter.PrivateData,
                SyntheticBroadcast.CaptionPid,
                DescriptorWriter.Loop(
                    PsiDescriptorWriter.StreamIdentifier(PsiDescriptorWriter.FirstCaptionComponentTag),
                    PsiDescriptorWriter.DataComponent(PsiDescriptorWriter.CaptionDataComponentId, 0x3D))),
            Windows(map));
        Assert.Contains(packets, packet => PidOf(packet) == SyntheticBroadcast.CaptionPid);
        Assert.Contains(packets, packet => PidOf(packet) == SyntheticBroadcast.SuperimposePid);
    }

    [Fact]
    public void ACaptionStatementStartsAPrivateStreamPacketStampedWithItsPts()
    {
        byte[] pes = PesWriter.PrivateStream(90_000L * 3 / 2, CaptionWriter.Carried(
            CaptionWriter.CaptionDataIdentifier,
            CaptionWriter.Statement(CaptionWriter.Positioned(
                7,
                2,
                new AribTextWriter().Kanji(string.Concat(Enumerable.Repeat("字幕", 80)))))));

        Assert.Equal([0x00, 0x00, 0x01, PesWriter.PrivateStream1], pes[..4]);
        Assert.Equal(pes.Length - 6, (pes[4] << 8) | pes[5]);
        Assert.Equal(0x84, pes[6]);
        Assert.Equal(135_000L, PtsOf(pes.AsSpan(9, 5)));
        Assert.Equal(CaptionWriter.CaptionDataIdentifier, pes[14]);
        Assert.Equal(CaptionWriter.StatementGroup << 2, pes[17]);

        byte[] packets = PesWriter.Packets(SyntheticBroadcast.CaptionPid, pes);

        Assert.Equal(2 * TransportStreamWriter.PacketSize, packets.Length);
        Assert.Equal(0x40, packets[1] & 0x40);
        Assert.Equal(0, packets[TransportStreamWriter.PacketSize + 1] & 0x40);
        Assert.Equal(0x30, packets[TransportStreamWriter.PacketSize + 3] & 0x30);
    }

    [Fact]
    public void ADataGroupChecksToZeroWhenReadBackOverItsOwnChecksum()
    {
        byte[] management = CaptionWriter.Management();
        byte[] statement = CaptionWriter.Statement(CaptionWriter.Positioned(1, 1, new AribTextWriter().Kanji("合成")));

        Assert.Equal(0, ReferenceCrc16.Compute(management));
        Assert.Equal(0, ReferenceCrc16.Compute(statement));
        Assert.Equal(CaptionWriter.ManagementGroup << 2, management[0]);
        Assert.Equal("jpn"u8.ToArray(), management[8..11]);
        Assert.Equal([0x1F, 0x20], statement[9..11]);
        Assert.Equal([CaptionWriter.ClearScreen, CaptionWriter.ActivePositionSet, 0x41, 0x41], statement[14..18]);
    }

    [Fact]
    public void ASilentDualMonoFrameIsAnAdtsFrameOfTwoSingleChannelElements()
    {
        byte[] frame = DualMonoAdts.SilentFrame();

        Assert.Equal(0xFF, frame[0]);
        Assert.Equal(0xF0, frame[1] & 0xF0);
        Assert.Equal(frame.Length, ((frame[3] & 0x03) << 11) | (frame[4] << 3) | (frame[5] >> 5));
        Assert.Equal(0, (frame[2] & 0x01) << 2 | (frame[3] >> 6));
        Assert.Equal(141 * frame.Length, DualMonoAdts.Silence(TimeSpan.FromSeconds(3)).Length);
    }

    [Fact]
    public void TheMeasuredBroadcastIsBuiltBitExactFromAPictureASoundAndTheSideInformation()
    {
        IReadOnlyList<string> arguments = SyntheticBroadcast.AsMeasured().Arguments("side.ts", null, "out.ts");

        Assert.Contains("+bitexact", arguments);
        Assert.Equal("testsrc2=size=1440x1080:rate=30000/1001", arguments[After(arguments, "-i")]);
        Assert.Equal("side.ts", arguments[After(arguments, "-i", 2)]);
        Assert.Equal(["0:v", "1:a", "2:s:0", "2:d:0"], Mapped(arguments));
        Assert.Contains("mpeg2video", arguments);
        Assert.Contains("setfield=tff", arguments);
        Assert.Equal("1040", arguments[After(arguments, "-mpegts_service_id")]);
        Assert.Equal("0", arguments[After(arguments, "-mpegts_m2ts_mode")]);
        Assert.Equal("out.ts", arguments[^1]);
    }

    [Fact]
    public void EachSoundIsAskedForInItsOwnWay()
    {
        Assert.Equal(["-c:a", "aac", "-ac", "1"], Tail(SyntheticBroadcast.Sounding(SyntheticSound.Mono).Arguments("s", null, "o"), "-c:a", 4));
        Assert.Contains("aformat=channel_layouts=5.1", SyntheticBroadcast.Sounding(SyntheticSound.Surround).Arguments("s", null, "o"));

        IReadOnlyList<string> bilingual = SyntheticBroadcast.Sounding(SyntheticSound.TwoLanguages).Arguments("s", null, "o");

        Assert.Equal(["0:v", "1:a", "2:a", "3:s:0", "3:d:0"], Mapped(bilingual));
        Assert.Contains("language=jpn", bilingual);
        Assert.Contains("language=eng", bilingual);

        IReadOnlyList<string> dual = SyntheticBroadcast.Sounding(SyntheticSound.DualMono).Arguments("s", "dual.aac", "o");

        Assert.Equal("aac", dual[After(dual, "-f", 1)]);
        Assert.Equal("dual.aac", dual[After(dual, "-i", 1)]);
        Assert.Equal("copy", dual[After(dual, "-c:a")]);
        Assert.DoesNotContain("-ac", dual);
    }

    [Fact]
    public void ABroadcastWithoutAPictureOrSideInformationAsksForNeither()
    {
        IReadOnlyList<string> arguments = new SyntheticBroadcast
        {
            Picture = SyntheticPicture.None,
            WithCaptions = false,
            WithSuperimpose = false,
        }.Arguments(null, null, "o");

        Assert.Equal(["0:a"], Mapped(arguments));
        Assert.DoesNotContain("-c:v", arguments);
        Assert.DoesNotContain("-c:s", arguments);
        Assert.DoesNotContain("testsrc2=size=1440x1080:rate=30000/1001", arguments);
    }

    [Fact]
    public void TheSideInformationAndTheSoundFileAreHandedOverExactlyWhenTheyAreCalledFor()
    {
        Assert.Throws<ArgumentException>(() => SyntheticBroadcast.AsMeasured().Arguments(null, null, "o"));
        Assert.Throws<ArgumentException>(() => SyntheticBroadcast.AsMeasured().Arguments("s", "dual.aac", "o"));
        Assert.Throws<ArgumentException>(() => SyntheticBroadcast.Sounding(SyntheticSound.DualMono).Arguments("s", null, "o"));
    }

    [Fact]
    public void TheEncodedCounterpartTakesTheServiceOwnPictureAndSoundsIntoH264()
    {
        IReadOnlyList<string> arguments = SyntheticBroadcast.EncodingArguments("in.m2ts", "out.mp4", 1040);

        Assert.Equal(["p:1040:v:0", "p:1040:a"], Mapped(arguments));
        Assert.Equal("libx264", arguments[After(arguments, "-c:v")]);
        Assert.Equal("copy", arguments[After(arguments, "-c:a")]);
        Assert.Equal("out.mp4", arguments[^1]);
    }

    private static List<byte[]> Packets(byte[] bytes)
        => [.. Enumerable.Range(0, bytes.Length / TransportStreamWriter.PacketSize)
            .Select(at => bytes[(at * TransportStreamWriter.PacketSize)..((at + 1) * TransportStreamWriter.PacketSize)])];

    private static int PidOf(byte[] packet) => ((packet[1] & 0x1F) << 8) | packet[2];

    private static byte[] Section(byte[] packet)
    {
        int pointer = packet[4];
        byte[] section = packet[(5 + pointer)..];
        int length = ((section[1] & 0x0F) << 8) | section[2];

        return section[..(3 + length)];
    }

    private static IEnumerable<byte[]> Windows(byte[] bytes)
        => Enumerable.Range(0, bytes.Length)
            .SelectMany(start => Enumerable.Range(1, bytes.Length - start).Select(length => bytes[start..(start + length)]));

    private static long PtsOf(ReadOnlySpan<byte> stamp)
        => ((long)(stamp[0] & 0x0E) << 29)
            | ((long)stamp[1] << 22)
            | ((long)(stamp[2] & 0xFE) << 14)
            | ((long)stamp[3] << 7)
            | ((long)(stamp[4] & 0xFE) >> 1);

    private static int After(IReadOnlyList<string> arguments, string flag, int skip = 0)
    {
        int at = -1;

        for (int seen = 0; seen <= skip; seen++)
        {
            at = arguments.ToList().IndexOf(flag, at + 1);
        }

        Assert.NotEqual(-1, at);

        return at + 1;
    }

    private static string[] Mapped(IReadOnlyList<string> arguments)
        => [.. arguments.Select((argument, at) => (argument, at))
            .Where(held => held.argument is "-map")
            .Select(held => arguments[held.at + 1])];

    private static string[] Tail(IReadOnlyList<string> arguments, string flag, int count)
        => [.. arguments.Skip(After(arguments, flag) - 1).Take(count)];
}
