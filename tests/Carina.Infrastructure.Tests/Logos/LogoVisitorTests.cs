using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Infrastructure.Logos;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Logos;

public sealed class LogoVisitorTests
{
    private const int SomeNetworkId = 32736;
    private const int SomeTransportStreamId = 32737;
    private const int SomeServiceId = 1024;
    private const int SomeLogoId = 261;
    private const int ASmallPictureType = 0x01;
    private const int APictureTypeInTheMiddle = 0x03;
    private const int TheHighestPictureType = 0x05;
    private const int SegmentSize = TransportStreamWriter.PacketSize * 24;

    private static readonly TuningParameters SomeTuning = TuningParameters.Terrestrial(27);

    private static readonly LogoSweepSettings Settings = new() { LongestVisit = TimeSpan.FromMinutes(6) };

    [Fact]
    public async Task ASmallPictureThatArrivesFirstGivesWayToTheLargeOneBehindIt()
    {
        PacedStream air = OnTheAir([TheLinks(), .. EveryPictureAsItComesOffTheAir()]);
        var visitor = new LogoVisitor(Driver(air), Settings, new HandTurnedClock());

        Task<LogoVisitResult> visiting = visitor.VisitAsync(Transport(), CancellationToken.None);

        air.Allow(7);

        LogoVisitResult visit = await visiting;

        Assert.Equal(LogoVisitOutcome.Collected, visit.Outcome);
        Assert.Equal(72, Assert.Single(visit.Logos).Image.Width);
    }

    [Fact]
    public async Task AVisitStopsOnceEveryLogoHasComeInEveryPictureTypeTheStandardDefines()
    {
        PacedStream air = OnTheAir([TheLinks(), .. EveryPictureAsItComesOffTheAir()]);
        var visitor = new LogoVisitor(Driver(air), Settings, new HandTurnedClock());

        Task<LogoVisitResult> visiting = visitor.VisitAsync(Transport(), CancellationToken.None);

        air.Allow(7);

        await visiting;

        Assert.Equal(7, air.Reads);
    }

    [Fact]
    public async Task AVisitReadsPastThePictureWithTheHighestTypeNumberBecauseALargerOneFollowsIt()
    {
        PacedStream air = OnTheAir([TheLinks(), .. EveryPictureAsItComesOffTheAir()]);
        var visitor = new LogoVisitor(Driver(air), Settings, new HandTurnedClock());

        Task<LogoVisitResult> visiting = visitor.VisitAsync(Transport(), CancellationToken.None);

        air.Allow(3);
        air.AwaitParkedBefore(4);

        Assert.Equal(3, air.Reads);

        air.Allow(4);

        await visiting;
    }

    [Fact]
    public async Task AVisitThatNeverSeesEveryPictureTypeReadsOnToItsDeadlineAndKeepsTheLargestItSaw()
    {
        PacedStream air = OnTheAir([
            TheLinks(),
            Picture(ASmallPictureType, 36, 24),
            Picture(TheHighestPictureType, 64, 36),
            Picture(APictureTypeInTheMiddle, 72, 36),
        ]);
        var clock = new HandTurnedClock();
        var visitor = new LogoVisitor(Driver(air), Settings, clock);

        Task<LogoVisitResult> visiting = visitor.VisitAsync(Transport(), CancellationToken.None);

        air.Allow(4);
        air.AwaitParkedBefore(5);
        clock.Turn(Settings.LongestVisit);

        LogoVisitResult visit = await visiting;

        Assert.Equal(LogoVisitOutcome.Collected, visit.Outcome);
        Assert.Equal(72, Assert.Single(visit.Logos).Image.Width);
    }

    [Fact]
    public async Task AVisitCutShortAfterOnlyTheSmallPictureStillKeepsIt()
    {
        PacedStream air = OnTheAir([TheLinks(), .. EveryPictureAsItComesOffTheAir()]);
        var visitor = new LogoVisitor(Driver(air), Settings, new HandTurnedClock());
        using var interrupting = new CancellationTokenSource();

        Task<LogoVisitResult> visiting = visitor.VisitAsync(Transport(), interrupting.Token);

        air.Allow(2);
        air.AwaitParkedBefore(3);
        await interrupting.CancelAsync();

        LogoVisitResult visit = await visiting;

        Assert.Equal(36, Assert.Single(visit.Logos).Image.Width);
    }

    [Fact]
    public async Task AVisitTakenAwayPartWayThroughIsCarriedOverRatherThanCountedAsFinished()
    {
        PacedStream air = OnTheAir([TheLinks(), .. EveryPictureAsItComesOffTheAir()]);
        var visitor = new LogoVisitor(Driver(air), Settings, new HandTurnedClock());
        using var interrupting = new CancellationTokenSource();

        Task<LogoVisitResult> visiting = visitor.VisitAsync(Transport(), interrupting.Token);

        air.Allow(2);
        air.AwaitParkedBefore(3);
        await interrupting.CancelAsync();

        LogoVisitResult visit = await visiting;

        Assert.Equal(LogoVisitOutcome.Interrupted, visit.Outcome);
    }

    [Fact]
    public async Task AVisitThatReachesItsDeadlineKeepsTheSmallPictureRatherThanLosingIt()
    {
        PacedStream air = OnTheAir([TheLinks(), Picture(ASmallPictureType, 36, 24)]);
        var clock = new HandTurnedClock();
        var visitor = new LogoVisitor(Driver(air), Settings, clock);

        Task<LogoVisitResult> visiting = visitor.VisitAsync(Transport(), CancellationToken.None);

        air.Allow(2);
        air.AwaitParkedBefore(3);
        clock.Turn(Settings.LongestVisit);

        LogoVisitResult visit = await visiting;

        Assert.Equal(LogoVisitOutcome.Collected, visit.Outcome);
        Assert.Equal(36, Assert.Single(visit.Logos).Image.Width);
    }

    private static BroadcastStream Transport()
        => new(
            new NetworkId(SomeNetworkId),
            new TransportStreamId(SomeTransportStreamId),
            SomeTuning,
            [new ServiceId(SomeServiceId)]);

    private static ScriptedDriverClient Driver(PacedStream air)
        => new ScriptedDriverClient().Script(SomeTuning, new ChannelScript { Paced = () => air });

    private static PacedStream OnTheAir(byte[][] segments)
        => PacedStream.InChunksOf([.. segments.SelectMany(Padded)], SegmentSize);

    private static byte[] Padded(byte[] segment)
    {
        var quiet = new TransportStreamWriter(TransportPacket.NullPacketPid);

        while (segment.Length + (quiet.Packets.Count * TransportStreamWriter.PacketSize) < SegmentSize)
        {
            quiet.AdaptationOnlyPacket();
        }

        return [.. segment, .. quiet.Bytes];
    }

    private static byte[] TheLinks()
        => new TransportStreamWriter(ServiceDescriptionTable.Pid)
            .Sections(new SectionWriter
            {
                TableId = ServiceDescriptionTable.ActualStreamTableId,
                TableIdExtension = SomeTransportStreamId,
                Body = new SdtWriter
                {
                    OriginalNetworkId = SomeNetworkId,
                    Services = [SdtWriter.Service(SomeServiceId, SiDescriptorWriter.LogoNamedOnly(SomeLogoId))],
                }.ToBody(),
            }.ToBytes())
            .Bytes;

    private static byte[][] EveryPictureAsItComesOffTheAir()
        => [
            Picture(ASmallPictureType, 36, 24),
            Picture(TheHighestPictureType, 64, 36),
            Picture(APictureTypeInTheMiddle, 72, 36),
            .. CarriedLogo.EveryPictureType
                .Where(type => type is not (ASmallPictureType or APictureTypeInTheMiddle or TheHighestPictureType))
                .Select(type => Picture(type, 36, 24)),
        ];

    private static byte[] Picture(int logoType, int width, int height)
        => new TransportStreamWriter(CommonDataTable.Pid)
            .Sections(new SectionWriter
            {
                TableId = CommonDataTable.TableId,
                TableIdExtension = 1,
                Body = new CdtWriter
                {
                    OriginalNetworkId = SomeNetworkId,
                    DataModule = CdtWriter.LogoModule(
                        logoType,
                        SomeLogoId,
                        3,
                        new LogoPngWriter { Width = width, Height = height }.ToBytes()),
                }.ToBody(),
            }.ToBytes())
            .Bytes;
}
