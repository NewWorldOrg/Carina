using Carina.Broadcast.Descriptors;
using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.Infrastructure.Scanning;

namespace Carina.Infrastructure.Tests.Scanning;

public sealed class ScanFailureClassificationTests
{
    private const int SomeStreamId = 50002;
    private const int AnotherStreamId = 50003;
    private const int SomeServiceId = 50101;
    private const int SomeBsSlot = 9;

    private static readonly TuningParameters Channel53 = TuningParameters.Terrestrial(53);
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task AFrontendThatNeverLocksIsRecordedApartFromEveryOtherFailure()
    {
        var outcome = await ScanOne(ChannelScript.NoLock());

        Assert.Equal(ScanAttemptOutcome.NoLock, Single(outcome).Outcome);
    }

    [Fact]
    public async Task ADriverThatRefusesTheTuneIsAFailureToLockRatherThanAnEmptyStream()
    {
        var outcome = await ScanOne(new ChannelScript
        {
            Refusal = new DriverProblem("noDeviceOfThatKind", ["No tuner reaches that system."]),
        });

        var attempt = Single(outcome);

        Assert.Equal(ScanAttemptOutcome.NoLock, attempt.Outcome);
        Assert.Contains("noDeviceOfThatKind", attempt.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALockedTunerWhoseDemuxDeliversNothingIsRecordedAsLockedWithoutData()
    {
        var outcome = await ScanOne(ChannelScript.Silent());

        var attempt = Single(outcome);

        Assert.Equal(ScanAttemptOutcome.LockedWithoutData, attempt.Outcome);
        Assert.True(attempt.Measurement!.Locked);
    }

    [Fact]
    public async Task BytesThatNeverCompleteTheTablesAreRecordedAsIncompleteRatherThanAsSilence()
    {
        var outcome = await ScanOne(ChannelScript.Carrying(new SyntheticStream
        {
            NetworkId = SyntheticStream.SomeNetworkId,
            TransportStreamId = SomeStreamId,
            Services = [new SyntheticService(SomeServiceId, "Carina One")],
            WithoutDescription = true,
        }));

        var attempt = Single(outcome);

        Assert.Equal(ScanAttemptOutcome.IncompleteTables, attempt.Outcome);
        Assert.Contains("service description table", attempt.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingNetworkTableIsIncompleteEvenWhenTheServicesAreAllThere()
    {
        var outcome = await ScanOne(ChannelScript.Carrying(new SyntheticStream
        {
            NetworkId = SyntheticStream.SomeNetworkId,
            TransportStreamId = SomeStreamId,
            Services = [new SyntheticService(SomeServiceId, "Carina One")],
            WithoutNetwork = true,
        }));

        var attempt = Single(outcome);

        Assert.Equal(ScanAttemptOutcome.IncompleteTables, attempt.Outcome);
        Assert.Contains("network information table", attempt.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASlotThatSilentlyCarriesAnotherStreamParsesCleanlyAndIsStillAFailure()
    {
        var slot = TuningParameters.Bs(SomeBsSlot, new TransportStreamId(SomeStreamId));
        var harness = new ScanHarness(new ScriptedDriverClient().Script(
            slot,
            ChannelScript.Carrying(SyntheticStream.Carrying(
                AnotherStreamId,
                new SyntheticService(SomeServiceId, "Carina One")))));

        var outcome = await harness.Orchestrator.RunAsync(ScanScope.Over([slot]), Cancel);
        var attempt = Single(outcome);

        Assert.Equal(ScanAttemptOutcome.UnexpectedStream, attempt.Outcome);
        Assert.Equal(AnotherStreamId, attempt.ObservedTransportStreamId!.Value);
        Assert.Contains($"stream {SomeStreamId}", attempt.Detail!, StringComparison.Ordinal);
        Assert.Contains($"stream {AnotherStreamId}", attempt.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TablesThatParseButDisagreeWithEachOtherAreRecordedAsAnUnexpectedStream()
    {
        var outcome = await ScanOne(ChannelScript.Carrying(new SyntheticStream
        {
            NetworkId = SyntheticStream.SomeNetworkId,
            TransportStreamId = SomeStreamId,
            TransportStreamIdInNetwork = AnotherStreamId,
            Services = [new SyntheticService(SomeServiceId, "Carina One")],
        }));

        var attempt = Single(outcome);

        Assert.Equal(ScanAttemptOutcome.UnexpectedStream, attempt.Outcome);
        Assert.Equal(SomeStreamId, attempt.ObservedTransportStreamId!.Value);
    }

    [Fact]
    public async Task TheFourFailuresAreToldApartInsteadOfCollapsingIntoOneError()
    {
        var driver = new ScriptedDriverClient()
            .Script(TuningParameters.Terrestrial(53), ChannelScript.NoLock())
            .Script(TuningParameters.Terrestrial(55), ChannelScript.Silent())
            .Script(TuningParameters.Terrestrial(57), ChannelScript.Carrying(new SyntheticStream
            {
                NetworkId = SyntheticStream.SomeNetworkId,
                TransportStreamId = SomeStreamId,
                Services = [new SyntheticService(SomeServiceId, "Carina One")],
                WithoutDescription = true,
            }))
            .Script(TuningParameters.Bs(SomeBsSlot, new TransportStreamId(SomeStreamId)),
                ChannelScript.Carrying(SyntheticStream.Carrying(
                    AnotherStreamId,
                    new SyntheticService(SomeServiceId, "Carina One"))));

        var outcome = await new ScanHarness(driver).Orchestrator.RunAsync(
            ScanScope.Over([
                TuningParameters.Terrestrial(53),
                TuningParameters.Terrestrial(55),
                TuningParameters.Terrestrial(57),
                TuningParameters.Bs(SomeBsSlot, new TransportStreamId(SomeStreamId)),
            ]),
            Cancel);

        Assert.Equal(
            [
                ScanAttemptOutcome.NoLock,
                ScanAttemptOutcome.LockedWithoutData,
                ScanAttemptOutcome.IncompleteTables,
                ScanAttemptOutcome.UnexpectedStream,
            ],
            outcome.Failures.Select(attempt => attempt.Outcome));
        Assert.Equal(4, outcome.Failures.Select(attempt => attempt.Detail).Distinct().Count());
    }

    [Fact]
    public async Task AStreamThatCarriesBothTablesForTheStreamAskedForSucceeds()
    {
        var outcome = await ScanOne(ChannelScript.Carrying(SyntheticStream.Carrying(
            SomeStreamId,
            new SyntheticService(SomeServiceId, "Carina One"))));

        var attempt = Single(outcome);

        Assert.Equal(ScanAttemptOutcome.Succeeded, attempt.Outcome);
        Assert.Empty(outcome.Failures);
        Assert.Equal(SomeStreamId, attempt.ObservedTransportStreamId!.Value);
    }

    [Fact]
    public async Task AnAttemptCarriesWhatTheTunerMeasuredWhileItWasStreaming()
    {
        var outcome = await ScanOne(ChannelScript.Carrying(SyntheticStream.Carrying(
            SomeStreamId,
            new SyntheticService(SomeServiceId, "Carina One"))));

        var attempt = Single(outcome);

        Assert.NotNull(attempt.Measurement);
        Assert.True(attempt.Measurement.Locked);
        Assert.Equal(21_500, attempt.Measurement.CnrMilliDecibels);
    }

    [Fact]
    public async Task AOneSegServiceIsProposedUnderItsOwnCategoryRatherThanAsTelevision()
    {
        var outcome = await ScanOne(ChannelScript.Carrying(new SyntheticStream
        {
            NetworkId = SyntheticStream.SomeNetworkId,
            TransportStreamId = SomeStreamId,
            Services =
            [
                new SyntheticService(SomeServiceId, "Carina One"),
                new SyntheticService(50108, "Carina One Mobile", ServiceKind.Television, PartiallyReceived: true),
            ],
        }));

        Assert.Equal(
            [ServiceCategory.Television, ServiceCategory.OneSeg],
            outcome.Difference.Added.Select(change => change.Category));
    }

    [Fact]
    public async Task EveryScanSessionIsClosedEvenWhenTheChannelCarriesNothing()
    {
        var driver = new ScriptedDriverClient().Script(Channel53, ChannelScript.Silent());
        var harness = new ScanHarness(driver);

        await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);

        Assert.Single(driver.Stopped);
    }

    private static async Task<ScanOutcome> ScanOne(ChannelScript script)
    {
        var harness = new ScanHarness(new ScriptedDriverClient().Script(Channel53, script));

        return await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
    }

    private static ScanRunAttempt Single(ScanOutcome outcome) => Assert.Single(outcome.Attempts);
}
