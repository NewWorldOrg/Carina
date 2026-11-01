using System.Net;
using System.Text.Json;

using Carina.Contracts;
using Carina.TestSupport;

namespace Carina.Api.Tests.FeatureTest;

public sealed class HardwareInfoEndpointTests
{
    private static readonly Uri Status = new("/api/driver/status", UriKind.Relative);
    private static readonly Uri Tuners = new("/api/tuners", UriKind.Relative);
    private static readonly Uri Detected = new("/api/tuners/detected", UriKind.Relative);
    private static readonly Uri CardReader = new("/api/driver/card-reader", UriKind.Relative);

    private static readonly DateTimeOffset Moment = new(2026, 11, 1, 19, 20, 0, TimeSpan.FromHours(9));

    private static readonly TunerHardwareDto Hardware = new()
    {
        Name = "Example ISDB-T frontend",
        KernelDriver = "example_dvb",
        Bus = TunerBus.Pci,
        DevicePath = "/dev/dvb/adapter0/frontend0",
        Present = true,
        LastSeenAt = Moment,
    };

    [Fact]
    public async Task TheDriverStatusCarriesTheDriversVersionStartAndName()
    {
        DriverHello hello = Capable() with { Version = "9.8.7", StartedAt = Moment, Name = "living-room" };
        await using DriverFeature feature = await DriverFeature.StartAsync(hello, Stocked);

        JsonElement data = await feature.UntilConnectionIs("connected");
        JsonElement greeting = data.GetProperty("hello");

        Assert.Equal("9.8.7", greeting.GetProperty("version").GetString());
        Assert.Equal(Moment, greeting.GetProperty("startedAt").GetDateTimeOffset());
        Assert.Equal("living-room", greeting.GetProperty("name").GetString());
    }

    [Fact]
    public async Task AnOldDriverLeavesTheVersionStartAndNameEmpty()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        JsonElement greeting = (await feature.UntilConnectionIs("connected")).GetProperty("hello");

        Assert.Equal(JsonValueKind.Null, greeting.GetProperty("version").ValueKind);
        Assert.Equal(JsonValueKind.Null, greeting.GetProperty("startedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, greeting.GetProperty("name").ValueKind);
    }

    [Fact]
    public async Task AnObservedTunerCarriesItsHardware()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        JsonElement tuner = await ObservedAsync(feature, "adapter0");
        JsonElement hardware = tuner.GetProperty("hardware");

        Assert.Equal(JsonValueKind.Null, tuner.GetProperty("machine").ValueKind);
        Assert.Equal("Example ISDB-T frontend", hardware.GetProperty("name").GetString());
        Assert.Equal("example_dvb", hardware.GetProperty("kernelDriver").GetString());
        Assert.Equal("pci", hardware.GetProperty("bus").GetString());
        Assert.Equal("/dev/dvb/adapter0/frontend0", hardware.GetProperty("devicePath").GetString());
        Assert.Equal(JsonValueKind.Null, hardware.GetProperty("usbPort").ValueKind);
        Assert.True(hardware.GetProperty("present").GetBoolean());
        Assert.Equal(Moment, hardware.GetProperty("lastSeenAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task ATunerFromAnOldDriverHasNoHardware()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        Assert.Equal(JsonValueKind.Null, (await ObservedAsync(feature, "adapter1")).GetProperty("hardware").ValueKind);
    }

    [Fact]
    public async Task ATunerRecordingIsNamedForTheProgrammeItRecords()
    {
        SessionId session = SessionId.Parse("rec-0123456789abcdef0123456789abcdef");
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), driver =>
        {
            Stocked(driver);
            driver.Tuners =
            [
                new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Busy, session)
                {
                    CurrentSession = new CurrentSessionDto { SessionId = session, Purpose = SessionPurpose.Recording },
                },
            ];
        });
        feature.Titles.Titles[session] = "Example Programme";

        JsonElement tuner = await ObservedAsync(feature, "adapter0");

        Assert.Equal("Example Programme", tuner.GetProperty("sessionTitle").GetString());
    }

    [Fact]
    public async Task NoRecordingIsLookedUpWhileNoTunerRecords()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        JsonElement tuner = await ObservedAsync(feature, "adapter0");

        Assert.Equal(JsonValueKind.Null, tuner.GetProperty("sessionTitle").ValueKind);
        Assert.Equal(0, feature.Titles.Asked);
    }

    [Fact]
    public async Task ADetectedDeviceCarriesItsHardware()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using HttpResponseMessage response = await feature.Client.GetAsync(Detected);
        (HttpStatusCode status, JsonElement body) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement device = body.GetProperty("data").GetProperty("devices")[0];
        Assert.Equal("Example ISDB-T frontend", device.GetProperty("hardware").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, device.GetProperty("machine").ValueKind);
    }

    [Fact]
    public async Task TheCardReaderIsAnsweredAsTheDriverSawIt()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), driver =>
        {
            Stocked(driver);
            driver.CardReader = new CardReaderDto
            {
                Use = CardReaderUse.Local,
                State = CardState.Inserted,
                Readers = [new CardReaderSlotDto { Name = "Example Reader", State = CardState.Inserted, InUse = true }],
                LeasesOut = 1,
                LastAnsweredAt = Moment,
                CheckedAt = Moment,
            };
        });

        using HttpResponseMessage response = await feature.Client.GetAsync(CardReader);
        (HttpStatusCode status, JsonElement body) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement data = body.GetProperty("data");
        Assert.Equal("local", data.GetProperty("use").GetString());
        Assert.Equal("inserted", data.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("problem").ValueKind);
        Assert.Equal("Example Reader", data.GetProperty("readers")[0].GetProperty("name").GetString());
        Assert.True(data.GetProperty("readers")[0].GetProperty("inUse").GetBoolean());
        Assert.Equal(1, data.GetProperty("leasesOut").GetInt32());
        Assert.Equal(Moment, data.GetProperty("lastAnsweredAt").GetDateTimeOffset());
        Assert.False(data.TryGetProperty("cardId", out _));
    }

    [Fact]
    public async Task ADriverThatDoesNotAnswerForItsReaderIsNotImplemented()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a", capabilities: [DriverCapabilities.TunerLedger]),
            Stocked);

        using HttpResponseMessage response = await feature.Client.GetAsync(CardReader);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Equal(0, feature.Driver.RequestsFor(DriverEndpoints.CardReader));
    }

    [Fact]
    public async Task ADriverThatCannotBeReachedLeavesTheReaderUnavailable()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync();

        using HttpResponseMessage response = await feature.Client.GetAsync(CardReader);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static DriverHello Capable() =>
        FakeDriver.HelloFor(
            "instance-a",
            capabilities:
            [
                DriverCapabilities.TunerLedger,
                DriverCapabilities.DeviceDetection,
                DriverCapabilities.DeviceHardware,
                DriverCapabilities.CardReaderStatus,
            ]);

    private static void Stocked(FakeDriver driver)
    {
        driver.Ledger = new TunerLedgerDto
        {
            Tuners = [new TunerConfigEntry { DeviceId = "adapter0" }, new TunerConfigEntry { DeviceId = "adapter1" }],
            SavedHash = "saved",
            LoadedHash = "saved",
        };
        driver.Tuners =
        [
            new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle) { Hardware = Hardware },
            new TunerSnapshot("adapter1", TunerKind.Terrestrial, TunerState.Idle),
        ];
        driver.DetectedDevices =
        [
            new DetectedDeviceDto
            {
                DeviceId = "adapter0",
                Detection = DeviceDetection.Detected,
                Kinds = [TunerKind.Terrestrial],
                Hardware = Hardware,
            },
        ];
    }

    private static async Task<JsonElement> ObservedAsync(DriverFeature feature, string deviceId)
    {
        using HttpResponseMessage response = await feature.Client.GetAsync(Tuners);
        (HttpStatusCode status, JsonElement body) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, status);

        return body.GetProperty("data").GetProperty("observed").EnumerateArray()
            .Single(tuner => tuner.GetProperty("deviceId").GetString() == deviceId);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return (response.StatusCode, document.RootElement.Clone());
    }
}
