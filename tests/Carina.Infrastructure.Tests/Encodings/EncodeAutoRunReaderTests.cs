using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Infrastructure.Encodings;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class EncodeAutoRunReaderTests
{
    private static readonly DateTime Settled = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly MachineSettings SixCores = new() { Cores = 6 };

    [Fact(DisplayName = "with no row settled the reader answers what the machine was deployed with, and says nobody settled it")]
    public async Task WithNoRowSettledTheReaderAnswersWhatTheMachineWasDeployedWith()
    {
        var rows = new HeldEncodeAutoRun();

        EncodeAutoRunStanding standing = await Reader(rows, new EncodeSettings { Automatically = false, MostCores = 3 }).ReadAsync(Cancel);

        Assert.False(standing.Automatically);
        Assert.Equal(3, standing.MostCores);
        Assert.False(standing.Stored);
        Assert.Null(standing.UpdatedAt);
    }

    [Fact(DisplayName = "a settled row is what the reader answers, and it says somebody settled it")]
    public async Task ASettledRowIsWhatTheReaderAnswers()
    {
        var rows = new HeldEncodeAutoRun();
        await rows.SaveAsync(EncodeAutoRun.Settled(true, 1, Settled), Cancel);

        EncodeAutoRunStanding standing = await Reader(rows, new EncodeSettings { Automatically = false, MostCores = 3 }).ReadAsync(Cancel);

        Assert.True(standing.Automatically);
        Assert.Equal(1, standing.MostCores);
        Assert.True(standing.Stored);
        Assert.Equal(Settled, standing.UpdatedAt);
    }

    [Fact(DisplayName = "BR-ED2-005: a deployed cap larger than this machine is answered as what will actually run")]
    public async Task ADeployedCapLargerThanThisMachineIsAnsweredAsWhatWillRun()
    {
        EncodeAutoRunStanding standing = await Reader(new HeldEncodeAutoRun(), new EncodeSettings { MostCores = 40 }).ReadAsync(Cancel);

        Assert.Equal(6, standing.MostCores);
    }

    [Fact(DisplayName = "BR-ED2-005: a settled cap larger than this machine is answered as what will actually run")]
    public async Task ASettledCapLargerThanThisMachineIsAnsweredAsWhatWillRun()
    {
        var rows = new HeldEncodeAutoRun();
        await rows.SaveAsync(EncodeAutoRun.Settled(true, EncodeAutoRun.MostCoresAnyMachineHas, Settled), Cancel);

        EncodeAutoRunStanding standing = await Reader(rows, new EncodeSettings { MostCores = 2 }).ReadAsync(Cancel);

        Assert.Equal(6, standing.MostCores);
        Assert.True(standing.Stored);
    }

    [Fact(DisplayName = "a row settled after the last look is the one the next look reads")]
    public async Task ARowSettledAfterTheLastLookIsTheOneTheNextLookReads()
    {
        var rows = new HeldEncodeAutoRun();
        IEncodeAutoRunReader reader = Reader(rows, new EncodeSettings { Automatically = true, MostCores = 2 });

        Assert.True((await reader.ReadAsync(Cancel)).Automatically);

        await rows.SaveAsync(EncodeAutoRun.Settled(false, 2, Settled), Cancel);

        Assert.False((await reader.ReadAsync(Cancel)).Automatically);
    }

    private static IEncodeAutoRunReader Reader(IEncodeAutoRunRepository rows, EncodeSettings deployed)
        => new EncodeAutoRunReader(rows, deployed, SixCores);
}
