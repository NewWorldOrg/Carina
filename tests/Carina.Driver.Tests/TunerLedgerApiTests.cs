using System.Net;
using System.Net.Http.Json;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Tuning;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Driver.Tests;

public sealed class TunerLedgerApiTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static CancellationToken Soon() => new CancellationTokenSource(Patience).Token;

    private static ScriptedTunerDetector TheFakeTuners() =>
        new(
            new TunerDetection(
                "fake-terrestrial",
                [DeviceKind.Terrestrial],
                DeviceDetection.Detected,
                null
            ),
            new TunerDetection(
                "fake-satellite",
                [DeviceKind.Satellite],
                DeviceDetection.Detected,
                null
            ),
            new TunerDetection(
                "fake-spare",
                [DeviceKind.Terrestrial],
                DeviceDetection.Detected,
                null
            )
        );

    private static Task<DriverUnderTest> Start(ITunerDetector? detector = null) =>
        DriverUnderTest.Start(
            reshapeServices: services =>
                services.AddSingleton<ITunerDetector>(detector ?? TheFakeTuners())
        );

    private static HttpContent Body(IReadOnlyList<TunerConfigEntry> entries) =>
        JsonContent.Create(entries, DriverJson.Context.IReadOnlyListTunerConfigEntry);

    private static HttpContent Body(TunerToggleRequest request) =>
        JsonContent.Create(request, DriverJson.Context.TunerToggleRequest);

    private static async Task<DriverProblem?> Refusal(HttpResponseMessage response) =>
        await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

    [Fact]
    public async Task TheLedgerAnswersWithTheTunersTheDriverWasStartedWith()
    {
        await using var driver = await Start();
        using var client = driver.Client();

        using var response = await client.GetAsync(DriverEndpoints.TunerLedger, Soon());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ledger = await DriverUnderTest.Read(response, DriverJson.Context.TunerLedgerDto);

        Assert.NotNull(ledger);
        Assert.Equal(
            ["fake-terrestrial", "fake-satellite", "fake-spare"],
            ledger.Tuners.Select(entry => entry.DeviceId)
        );
    }

    [Fact]
    public async Task ADriverRunningWhatIsSavedReportsNoDrift()
    {
        await using var driver = await Start();
        using var client = driver.Client();

        var ledger = await Ledger(client);

        Assert.False(ledger.HasDrifted());
        Assert.Equal(ledger.LoadedHash, ledger.SavedHash);
    }

    [Fact]
    public async Task ASavedLedgerIsNotTheRunningOneAndTheDriverSaysSo()
    {
        await using var driver = await Start();
        using var client = driver.Client();

        using var response = await client.PutAsync(
            DriverEndpoints.Tuners,
            Body([new TunerConfigEntry { DeviceId = "fake-terrestrial" }]),
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var saved = await DriverUnderTest.Read(response, DriverJson.Context.TunerLedgerDto);

        Assert.NotNull(saved);
        Assert.True(saved.HasDrifted());
        Assert.Equal("fake-terrestrial", Assert.Single(saved.Tuners).DeviceId);
        Assert.True(await Ledger(client) is { } later && later.HasDrifted());
    }

    [Fact]
    public async Task SavingTheLedgerDoesNotChangeTheTunersTheDriverIsServingWith()
    {
        await using var driver = await Start();
        using var client = driver.Client();

        using var response = await client.PutAsync(
            DriverEndpoints.Tuners,
            Body([new TunerConfigEntry { DeviceId = "fake-terrestrial" }]),
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tuners = await DriverUnderTest.Read(
            await client.GetAsync(DriverEndpoints.Tuners, Soon()),
            DriverJson.Context.IReadOnlyListTunerSnapshot
        );

        Assert.NotNull(tuners);
        Assert.Equal(
            ["fake-terrestrial", "fake-satellite", "fake-spare"],
            tuners.Select(tuner => tuner.DeviceId)
        );
    }

    [Fact]
    public async Task ALedgerWithNothingInItIsRefused()
    {
        await using var driver = await Start();
        using var client = driver.Client();

        using var response = await client.PutAsync(
            DriverEndpoints.Tuners,
            Body([]),
            Soon()
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("emptyLedger", (await Refusal(response))?.Title);
    }

    [Fact]
    public async Task RefusingAnEmptyLedgerSaysHowToClearItOnPurposeInstead()
    {
        await using var driver = await Start();
        using var client = driver.Client();

        using var response = await client.PutAsync(
            DriverEndpoints.Tuners,
            Body([]),
            Soon()
        );

        var problem = await Refusal(response);

        Assert.NotNull(problem);
        Assert.Contains(
            "detect",
            string.Join(" ", problem.Problems),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task AnEntryNamingATunerThatWasNeverDetectedIsRefusedAndSaysWhich()
    {
        await using var driver = await Start();
        using var client = driver.Client();

        using var response = await client.PutAsync(
            DriverEndpoints.Tuners,
            Body([new TunerConfigEntry { DeviceId = "adapter9.frontend0" }]),
            Soon()
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await Refusal(response);

        Assert.NotNull(problem);
        Assert.Equal("unknownDevice", problem.Title);
        Assert.Contains(
            "adapter9.frontend0",
            string.Join(" ", problem.Problems),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task NoDeviceNodeCrossesTheSocketWhenTheDriverRefusesALedger()
    {
        await using var driver = await Start();
        using var client = driver.Client();

        using var response = await client.PutAsync(
            DriverEndpoints.Tuners,
            Body([new TunerConfigEntry { DeviceId = "adapter9.frontend0" }]),
            Soon()
        );

        var body = await response.Content.ReadAsStringAsync(Soon());

        Assert.DoesNotContain("/dev", body, StringComparison.Ordinal);
        Assert.DoesNotContain("devicePath", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoDeviceNodeCrossesTheSocketWhenTheDriverAnswersWithTheLedger()
    {
        await using var driver = await Start();
        using var client = driver.Client();

        var body = await client.GetStringAsync(DriverEndpoints.TunerLedger, Soon());

        Assert.DoesNotContain("/dev", body, StringComparison.Ordinal);
        Assert.DoesNotContain("devicePath", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABodyThisDriverCannotReadIsRefusedRatherThanTakenAsAnEmptyLedger()
    {
        await using var driver = await Start();
        using var client = driver.Client();

        using var response = await client.PutAsync(
            DriverEndpoints.Tuners,
            new StringContent(
                "not json",
                System.Text.Encoding.UTF8,
                "application/json"
            ),
            Soon()
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("malformedRequest", (await Refusal(response))?.Title);
    }

    [Fact]
    public async Task AnEmptyBodyIsRefusedRatherThanTakenAsAnEmptyLedger()
    {
        await using var driver = await Start();
        using var client = driver.Client();

        using var response = await client.PutAsync(
            DriverEndpoints.Tuners,
            new StringContent("", System.Text.Encoding.UTF8, "application/json"),
            Soon()
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("malformedRequest", (await Refusal(response))?.Title);
    }

    [Fact]
    public async Task AnOperatorEditingTheFileUnderTheRunningDriverIsAnsweredAsDrift()
    {
        await using var driver = await Start();
        using var client = driver.Client();

        driver.RewriteLedger(
            driver.Configuration with
            {
                Devices =
                [
                    new DeviceSettings("fake-terrestrial", DeviceKind.Terrestrial),
                ],
            }
        );

        var ledger = await Ledger(client);

        Assert.True(ledger.HasDrifted());
        Assert.Equal("fake-terrestrial", Assert.Single(ledger.Tuners).DeviceId);
    }

    [Fact]
    public async Task ALedgerFileThatNoLongerParsesIsAnsweredAsDriftRatherThanAsAgreement()
    {
        await using var driver = await Start();
        using var client = driver.Client();

        driver.CorruptLedger();

        var ledger = await Ledger(client);

        Assert.Null(ledger.SavedHash);
        Assert.True(ledger.HasDrifted());
        Assert.NotEmpty(ledger.Tuners);
    }

    [Fact]
    public async Task TheLedgerIsReadFromDiskEachTimeSoThatDriftIsNoticedWithoutARestart()
    {
        await using var driver = await Start();
        using var client = driver.Client();

        Assert.False((await Ledger(client)).HasDrifted());

        driver.RewriteLedger(
            driver.Configuration with
            {
                Devices = [new DeviceSettings("fake-satellite", DeviceKind.Satellite)],
            }
        );

        Assert.True((await Ledger(client)).HasDrifted());
    }

    [Fact]
    public async Task TheLedgerPathIsNotMistakenForATunerCalledLedger()
    {
        await using var driver = await Start();
        using var client = driver.Client();

        using var response = await client.PatchAsync(
            DriverEndpoints.Tuner("ledger"),
            Body(new TunerToggleRequest { Disabled = true }),
            Soon()
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("noSuchTuner", (await Refusal(response))?.Title);
    }

    private static async Task<TunerLedgerDto> Ledger(HttpClient client)
    {
        using var response = await client.GetAsync(DriverEndpoints.TunerLedger, Soon());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ledger = await DriverUnderTest.Read(response, DriverJson.Context.TunerLedgerDto);

        Assert.NotNull(ledger);

        return ledger;
    }
}
