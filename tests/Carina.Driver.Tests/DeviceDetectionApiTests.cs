using System.Net;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Tuning;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Driver.Tests;

public sealed class DeviceDetectionApiTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static CancellationToken Soon() => new CancellationTokenSource(Patience).Token;

    [Fact]
    public async Task DetectedDevicesAnswerWithEveryTunerTheBackendFinds()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.DevicesDetected, Soon());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IReadOnlyList<DetectedDeviceDto>? detected = await DriverUnderTest.Read(
            response,
            DriverJson.Context.IReadOnlyListDetectedDeviceDto
        );

        Assert.NotNull(detected);
        Assert.Equal(
            ["fake-terrestrial", "fake-satellite", "fake-spare"],
            detected.Select(device => device.DeviceId)
        );
    }

    [Fact]
    public async Task ADetectedTunerSaysWhichDeliverySystemsItReceives()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        IReadOnlyList<DetectedDeviceDto> detected = await Ask(client);

        Assert.Equal(
            [TunerKind.Terrestrial],
            Assert.Single(detected, device => device.DeviceId is "fake-terrestrial").Kinds
        );
        Assert.Equal(
            [TunerKind.Satellite],
            Assert.Single(detected, device => device.DeviceId is "fake-satellite").Kinds
        );
        Assert.All(detected, device =>
            Assert.Equal(DeviceDetection.Detected, device.Detection)
        );
    }

    [Fact]
    public async Task DetectionReportsTheHardwareSoATunerTurnedOffInTheLedgerIsStillFound()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        IReadOnlyList<DetectedDeviceDto> detected = await Ask(client);

        Assert.Contains(detected, device => device.DeviceId is "fake-spare");
    }

    [Fact]
    public async Task NoDeviceNodeCrossesTheSocketWhenTheDriverReportsWhatItDetected()
    {
        var scripted = new ScriptedTunerDetector(
            new TunerDetection(
                "adapter0.frontend0",
                [DeviceKind.Terrestrial],
                DeviceDetection.Detected,
                null
            ),
            new TunerDetection(
                "adapter1.frontend0",
                [],
                DeviceDetection.Busy,
                "adapter1.frontend0: opening the frontend failed."
            )
        );

        await using DriverUnderTest driver = await DriverUnderTest.Start(
            reshapeServices: services => services.AddSingleton<ITunerDetector>(scripted)
        );
        using HttpClient client = driver.Client();

        string body = await client.GetStringAsync(DriverEndpoints.DevicesDetected, Soon());

        Assert.Contains("adapter0.frontend0", body, StringComparison.Ordinal);
        Assert.DoesNotContain("/dev", body, StringComparison.Ordinal);
        Assert.DoesNotContain("devicePath", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATunerAnotherProcessHoldsIsAnsweredWithItsReasonRatherThanLeftOut()
    {
        var scripted = new ScriptedTunerDetector(
            new TunerDetection(
                "adapter0.frontend0",
                [],
                DeviceDetection.Busy,
                "adapter0.frontend0: another process is already holding this tuner."
            )
        );

        await using DriverUnderTest driver = await DriverUnderTest.Start(
            reshapeServices: services => services.AddSingleton<ITunerDetector>(scripted)
        );
        using HttpClient client = driver.Client();

        DetectedDeviceDto device = Assert.Single(await Ask(client));

        Assert.Equal(DeviceDetection.Busy, device.Detection);
        Assert.Empty(device.Kinds);
        Assert.NotNull(device.Detail);
    }

    [Fact]
    public async Task DetectionIsAskedOfTheHardwareEachTimeRatherThanAnsweredFromStartup()
    {
        var scripted = new ScriptedTunerDetector(
            new TunerDetection(
                "adapter0.frontend0",
                [DeviceKind.Terrestrial],
                DeviceDetection.Detected,
                null
            )
        );

        await using DriverUnderTest driver = await DriverUnderTest.Start(
            reshapeServices: services => services.AddSingleton<ITunerDetector>(scripted)
        );
        using HttpClient client = driver.Client();

        int before = scripted.Detections;

        await Ask(client);
        await Ask(client);

        Assert.Equal(before + 2, scripted.Detections);
    }

    private static async Task<IReadOnlyList<DetectedDeviceDto>> Ask(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.DevicesDetected, Soon());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IReadOnlyList<DetectedDeviceDto>? detected = await DriverUnderTest.Read(
            response,
            DriverJson.Context.IReadOnlyListDetectedDeviceDto
        );

        Assert.NotNull(detected);

        return detected;
    }
}
