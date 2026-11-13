using System.Net;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Driver;
using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Integrity;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Recordings;

[SupportedOSPlatform("linux")]
[Trait("Category", "Feature")]
public sealed class DeleteIntegrityFindingActionTests
{
    private const string IdDescription =
        "A finding is named by the identifier the most recent check gave it, written as a GUID.";

    private static readonly RecordingId Recording = new(new Guid("7a3e5c1b-0000-0000-0000-00000000000c"));

    private static readonly RecordingId Kept = new(new Guid("7a3e5c1b-0000-0000-0000-0000000000a1"));

    private static readonly RecordingId Gone = new(new Guid("7a3e5c1b-0000-0000-0000-0000000000a2"));

    private static readonly OutputRoot Encodes = new("encodes");

    private static readonly string KeptFile = Kept.Wire + ".ts";

    private static readonly string KeptArtefact = Kept.Wire + ".0000000a000000000000000000000001.mp4";

    private static readonly string GoneArtefact = Gone.Wire + ".0000000a000000000000000000000001.mp4";

    [Fact]
    public async Task AFileNoRecordingOwnsIsThrownAwayByTheDriverAndItsFindingLeavesTheList()
    {
        using var store = new RecordingStore();
        store.Holding("leftover.tmp", 400).Holding("kept.bin", 10);
        await using IntegrityFeature feature = Removing(store);
        IntegrityFinding finding = await CheckedAsync(feature, "leftover.tmp");

        (HttpStatusCode status, JsonElement body) = await DeleteAsync(feature, finding.Id.Value.ToString());
        (_, JsonElement page) = await feature.GetAsync("/api/recordings/integrity");
        StrayFileErasureRequest asked = Assert.Single(feature.Driver.AskedAboutStrays);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(finding.Id.Value, body.GetProperty("data").GetProperty("findingId").GetGuid());
        Assert.Equal("leftover.tmp", body.GetProperty("data").GetProperty("path").GetString());
        Assert.Equal("primary", body.GetProperty("data").GetProperty("outputRoot").GetString());
        Assert.True(body.GetProperty("data").GetProperty("fileRemoved").GetBoolean());
        Assert.Equal("leftover.tmp", asked.Path);
        Assert.Equal(400, asked.SizeBytes);
        Assert.Equal(finding.LastWrittenAt, asked.LastWrittenAt.UtcDateTime);
        Assert.False(File.Exists(Path.Combine(store.Root, "leftover.tmp")));
        Assert.True(File.Exists(Path.Combine(store.Root, "kept.bin")));
        Assert.Equal(
            ["kept.bin"],
            page.GetProperty("data").GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("path").GetString()!)
                .ToArray());
    }

    [Fact]
    public async Task AFileThatChangedSinceTheCheckIsRefusedAndLeftAsItNowIs()
    {
        using var store = new RecordingStore();
        store.Holding("leftover.tmp", 400);
        await using IntegrityFeature feature = Removing(store);
        IntegrityFinding finding = await CheckedAsync(feature, "leftover.tmp");
        await File.AppendAllTextAsync(Path.Combine(store.Root, "leftover.tmp"), "more");

        (HttpStatusCode status, JsonElement body) = await DeleteAsync(feature, finding.Id.Value.ToString());
        (_, JsonElement page) = await feature.GetAsync("/api/recordings/integrity");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("fileChanged", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Empty(feature.Driver.AskedAboutStrays);
        Assert.Equal(404, new FileInfo(Path.Combine(store.Root, "leftover.tmp")).Length);
        Assert.Single(page.GetProperty("data").GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task AFileTheLedgerHasClaimedSinceTheCheckIsRefusedAndNothingIsAsked()
    {
        using var store = new RecordingStore();
        store.Holding("late.m2ts", 400);
        await using IntegrityFeature feature = Removing(store);
        IntegrityFinding finding = await CheckedAsync(feature, "late.m2ts");
        feature.Ledger.Rows.Add(LedgerFile.StillWriting(Recording, IntegrityFeature.Primary, new RecordingFileName("late.m2ts")));

        (HttpStatusCode status, JsonElement body) = await DeleteAsync(feature, finding.Id.Value.ToString());

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("fileChanged", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Empty(feature.Driver.AskedAboutStrays);
        Assert.True(File.Exists(Path.Combine(store.Root, "late.m2ts")));
    }

    [Fact]
    public async Task AFileEncodeWorkInHandHasDeclaredSinceTheCheckIsRefused()
    {
        using var store = new RecordingStore();
        store.Holding("work.part", 400);
        await using IntegrityFeature feature = Removing(store);
        IntegrityFinding finding = await CheckedAsync(feature, "work.part");
        feature.Working.Files.Add(new DeclaredFile(IntegrityFeature.Primary, "work.part"));

        (HttpStatusCode status, _) = await DeleteAsync(feature, finding.Id.Value.ToString());

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.True(File.Exists(Path.Combine(store.Root, "work.part")));
    }

    [Fact]
    public async Task AFindingAboutARecordingsOwnFileIsNotAWayToRemoveItAndSaysWhereToGoInstead()
    {
        await using var feature = new IntegrityFeature();
        IntegrityCheckId check = IntegrityCheckId.New();
        IntegrityFinding finding = IntegrityFinding.SizeDisagrees(
            check,
            IntegrityFeature.Primary,
            Recording,
            new RecordingFileName("one.m2ts"),
            1_000,
            999,
            IntegrityFeature.Noon);
        Save(feature, check, finding);

        (HttpStatusCode status, JsonElement body) = await DeleteAsync(feature, finding.Id.Value.ToString());

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("namesARecording", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Contains(Recording.Wire, body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Contains("DELETE /api/recordings/{id}", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Empty(feature.Driver.AskedAboutStrays);
    }

    [Fact]
    public async Task ARowWhoseFileIsMissingIsNotAFileToThrowAwayAndSaysSo()
    {
        await using var feature = new IntegrityFeature();
        IntegrityCheckId check = IntegrityCheckId.New();
        IntegrityFinding finding = IntegrityFinding.FileMissing(
            check,
            IntegrityFeature.Primary,
            Recording,
            new RecordingFileName("one.m2ts"),
            1_000,
            IntegrityFeature.Noon);
        Save(feature, check, finding);

        (HttpStatusCode status, JsonElement body) = await DeleteAsync(feature, finding.Id.Value.ToString());

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("nothingOnTheDisk", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Equal(finding.Id.Value, body.GetProperty("data").GetProperty("findingId").GetGuid());
    }

    [Fact]
    public async Task AFindingKeptWithoutTheTimeOfItsLastWriteIsRefusedUntilTheCheckRunsAgain()
    {
        await using var feature = new IntegrityFeature();
        IntegrityCheckId check = IntegrityCheckId.New();
        IntegrityFinding finding = IntegrityFinding.NoLedgerRow(
            check,
            IntegrityFeature.Primary,
            "leftover.tmp",
            400,
            IntegrityFeature.Noon);
        Save(feature, check, finding);

        (HttpStatusCode status, JsonElement body) = await DeleteAsync(feature, finding.Id.Value.ToString());

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("noTimeWasTaken", body.GetProperty("data").GetProperty("refusal").GetString());
    }

    [Fact]
    public async Task AFindingTheMostRecentCheckDidNotMakeIsNotFound()
    {
        await using var feature = new IntegrityFeature();
        IntegrityCheckId older = IntegrityCheckId.New();
        IntegrityFinding stale = IntegrityFinding.NoLedgerRow(
            older,
            IntegrityFeature.Primary,
            "leftover.tmp",
            400,
            IntegrityFeature.Noon,
            IntegrityFeature.Noon.AddHours(-1));
        Save(feature, older, stale);
        Save(feature, IntegrityCheckId.New());

        (HttpStatusCode stalely, JsonElement body) = await DeleteAsync(feature, stale.Id.Value.ToString());
        (HttpStatusCode unknown, _) = await DeleteAsync(feature, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.NotFound, stalely);
        Assert.Equal("noSuchFinding", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Equal(HttpStatusCode.NotFound, unknown);
    }

    [Theory]
    [InlineData("not-a-finding")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("..%2F..%2Fetc%2Fpasswd")]
    [InlineData("leftover.tmp")]
    public async Task SomethingThatIsNotAFindingIdIsRefusedBeforeAnythingIsRead(string named)
    {
        using var store = new RecordingStore();
        store.Holding("leftover.tmp", 400);
        await using IntegrityFeature feature = Removing(store);
        await CheckedAsync(feature, "leftover.tmp");

        (HttpStatusCode status, JsonElement body) = await DeleteAsync(feature, named);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(IdDescription, body.GetProperty("message").GetString());
        Assert.Empty(feature.Driver.AskedAboutStrays);
        Assert.True(File.Exists(Path.Combine(store.Root, "leftover.tmp")));
    }

    [Fact]
    public async Task APathOrARootSentBesideTheFindingIsNeverWhatIsThrownAway()
    {
        using var store = new RecordingStore();
        store.Holding("leftover.tmp", 400).Holding("precious.bin", 10);
        await using IntegrityFeature feature = Removing(store);
        IntegrityFinding finding = await CheckedAsync(feature, "leftover.tmp");

        using HttpResponseMessage response = await feature.Client.PostAsJsonAsync(
            new Uri($"/api/recordings/integrity/findings/{finding.Id.Value}/delete", UriKind.Relative),
            new { path = "precious.bin", outputRoot = "bulk", sizeBytes = 10 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("leftover.tmp", Assert.Single(feature.Driver.AskedAboutStrays).Path);
        Assert.Equal("primary", feature.Driver.AskedAboutStrays[0].OutputRoot);
        Assert.True(File.Exists(Path.Combine(store.Root, "precious.bin")));
    }

    [Fact]
    public async Task AFileAlreadyThrownAwayIsNotThrownAwayAgain()
    {
        using var store = new RecordingStore();
        store.Holding("leftover.tmp", 400).Holding("kept.bin", 10);
        await using IntegrityFeature feature = Removing(store);
        IntegrityFinding finding = await CheckedAsync(feature, "leftover.tmp");

        (HttpStatusCode first, _) = await DeleteAsync(feature, finding.Id.Value.ToString());
        (HttpStatusCode second, JsonElement body) = await DeleteAsync(feature, finding.Id.Value.ToString());

        Assert.Equal(HttpStatusCode.OK, first);
        Assert.Equal(HttpStatusCode.Conflict, second);
        Assert.Equal("alreadyThrownAway", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Single(feature.Driver.AskedAboutStrays);
    }

    [Fact]
    public async Task ARootTheDriverNoLongerDeclaresIsRefusedAndTheFileStays()
    {
        using var store = new RecordingStore();
        store.Holding("leftover.tmp", 400);
        await using IntegrityFeature feature = Removing(store);
        IntegrityFinding finding = await CheckedAsync(feature, "leftover.tmp");
        feature.Driver.Roots.Clear();

        (HttpStatusCode status, JsonElement body) = await DeleteAsync(feature, finding.Id.Value.ToString());

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("rootOutOfReach", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Empty(feature.Driver.AskedAboutStrays);
        Assert.True(File.Exists(Path.Combine(store.Root, "leftover.tmp")));
    }

    [Fact]
    public async Task ADriverThatCannotBeReachedIsAnsweredAsUnavailable()
    {
        using var store = new RecordingStore();
        store.Holding("leftover.tmp", 400);
        await using IntegrityFeature feature = Removing(store);
        IntegrityFinding finding = await CheckedAsync(feature, "leftover.tmp");
        feature.Driver.Unreachable = "the socket was not there";

        (HttpStatusCode status, JsonElement body) = await DeleteAsync(feature, finding.Id.Value.ToString());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("driverUnreachable", body.GetProperty("data").GetProperty("refusal").GetString());
    }

    [Fact]
    public async Task AFileTheDriverCouldNotRemoveIsAnsweredAsLeftBehindAndStaysListed()
    {
        using var store = new RecordingStore();
        store.Holding("leftover.tmp", 400);
        await using IntegrityFeature feature = Removing(store);
        IntegrityFinding finding = await CheckedAsync(feature, "leftover.tmp");
        feature.Driver.OnStrays = _ => DriverCall<StrayFileErasedDto>.Refused(
            new DriverProblem(SessionRefusalTitles.FileLeftBehind, ["permission denied"]));

        (HttpStatusCode status, JsonElement body) = await DeleteAsync(feature, finding.Id.Value.ToString());
        (_, JsonElement page) = await feature.GetAsync("/api/recordings/integrity");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("filesLeftBehind", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Single(page.GetProperty("data").GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task OnlyOneFileIsThrownAwayAtATimeAndTheOneWaitingIsToldWhichIsUnderway()
    {
        using var store = new RecordingStore();
        store.Holding("first.tmp", 400).Holding("second.tmp", 400);
        await using IntegrityFeature feature = Removing(store);
        IntegrityFinding first = await CheckedAsync(feature, "first.tmp");
        IntegrityFinding second = feature.Checks.Saved[^1].Findings.Single(finding => finding.Path == "second.tmp");
        TaskCompletionSource underway = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource refused = new(TaskCreationOptions.RunContinuationsAsynchronously);

        feature.Driver.OnStrays = request =>
        {
            underway.TrySetResult();
            refused.Task.Wait();

            return DriverCall<StrayFileErasedDto>.Reached(
                new StrayFileErasedDto { OutputRoot = request.OutputRoot, Path = request.Path, FileRemoved = true });
        };

        Task<(HttpStatusCode Status, JsonElement Body)> running = DeleteAsync(feature, first.Id.Value.ToString());

        await underway.Task;

        (HttpStatusCode status, JsonElement body) = await DeleteAsync(feature, second.Id.Value.ToString());

        refused.SetResult();

        Assert.Equal(HttpStatusCode.OK, (await running).Status);
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("oneIsAlreadyBeingThrownAway", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Contains(first.Id.Value.ToString(), body.GetProperty("message").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDocumentDescribesTheWayAFindingIsThrownAwayAsTakingNothingButTheFindingId()
    {
        await using var factory = new TestingWebApplicationFactory();
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        (string Path, string Method, JsonObject Value) operation = ServedOpenApi.Operations(document)
            .Single(candidate => candidate.Path == "/api/recordings/integrity/findings/{findingId}/delete");

        Assert.Equal("post", operation.Method);
        Assert.Null(operation.Value["requestBody"]);
        Assert.Equal(
            ["findingId"],
            operation.Value["parameters"]!.AsArray()
                .Select(parameter => $"{parameter!["name"]!.GetValue<string>()}")
                .ToArray());
        Assert.All(
            operation.Value["parameters"]!.AsArray(),
            parameter => Assert.Equal("path", parameter!["in"]!.GetValue<string>()));
    }

    private static IntegrityFeature Removing(RecordingStore store)
    {
        var feature = new IntegrityFeature(walking: store.Root);

        feature.Driver.Roots.Add(new StorageRootDto { Name = "primary", Writable = true });
        feature.Driver.OnStrays = request =>
        {
            File.Delete(Path.Combine(store.Root, request.Path));

            return DriverCall<StrayFileErasedDto>.Reached(
                new StrayFileErasedDto { OutputRoot = request.OutputRoot, Path = request.Path, FileRemoved = true });
        };

        return feature;
    }

    private static async Task<IntegrityFinding> CheckedAsync(IntegrityFeature feature, string path)
    {
        (HttpStatusCode ran, _) = await feature.PostAsync("/api/recordings/integrity/run");

        Assert.Equal(HttpStatusCode.OK, ran);

        return feature.Checks.Saved[^1].Findings.Single(finding => finding.Path == path);
    }

    private static void Save(IntegrityFeature feature, IntegrityCheckId check, params IntegrityFinding[] findings)
        => feature.Checks.Saved.Add(IntegrityReport.Of(
            IntegrityCheck.Rehydrate(check, IntegrityFeature.Noon, IntegrityFeature.Noon, 1, 0, 1, 1, 1, 0, 0),
            findings));

    private static Task<(HttpStatusCode Status, JsonElement Body)> DeleteAsync(IntegrityFeature feature, string named)
        => feature.PostAsync($"/api/recordings/integrity/findings/{named}/delete");

    [Fact]
    public async Task AFileNoRecordingOwnsIsReallyTakenOffTheDiskByTheDriverAndNothingBesideItIsTouched()
    {
        await using SyntheticDriverHost driver = await SyntheticDriverHost.StartAsync();
        string leftover = Holding(driver, "nested/leftover.tmp", 1_000);
        string kept = Holding(driver, "kept.bin", 10);
        await using var app = new AppAgainstTheDriver(driver);
        Guid finding = await app.FoundAsync("nested/leftover.tmp");

        (HttpStatusCode status, JsonElement body) = await app.PostAsync(
            $"/api/recordings/integrity/findings/{finding}/delete");
        (_, JsonElement page) = await app.GetAsync("/api/recordings/integrity");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("data").GetProperty("fileRemoved").GetBoolean());
        Assert.False(File.Exists(leftover));
        Assert.True(File.Exists(kept));
        Assert.DoesNotContain(
            "nested/leftover.tmp",
            page.GetProperty("data").GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("path").GetString()));
    }

    [Fact]
    public async Task AFileThatChangedAfterTheCheckIsRefusedWithAConflictAndKeepsItsNewBytes()
    {
        await using SyntheticDriverHost driver = await SyntheticDriverHost.StartAsync();
        string leftover = Holding(driver, "leftover.tmp", 1_000);
        await using var app = new AppAgainstTheDriver(driver);
        Guid finding = await app.FoundAsync("leftover.tmp");
        await File.AppendAllTextAsync(leftover, "written after the check");

        (HttpStatusCode status, JsonElement body) = await app.PostAsync(
            $"/api/recordings/integrity/findings/{finding}/delete");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("fileChanged", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.True(File.Exists(leftover));
        Assert.Equal(1_023, new FileInfo(leftover).Length);
    }

    [Fact]
    public async Task NoPathCanBeSlippedInBecauseTheOnlyThingTheRouteTakesIsTheFindingId()
    {
        await using SyntheticDriverHost driver = await SyntheticDriverHost.StartAsync();
        string leftover = Holding(driver, "leftover.tmp", 1_000);
        string precious = Holding(driver, "precious.bin", 10);
        string outside = Path.GetFullPath(Path.Combine(driver.RecordingsDirectory, "..", "outside.bin"));
        await File.WriteAllBytesAsync(outside, new byte[10]);
        await using var app = new AppAgainstTheDriver(driver);
        Guid finding = await app.FoundAsync("leftover.tmp");

        (HttpStatusCode traversal, _) = await app.PostAsync(
            "/api/recordings/integrity/findings/..%2F..%2Foutside.bin/delete");

        using HttpResponseMessage carrying = await app.Client.PostAsJsonAsync(
            new Uri($"/api/recordings/integrity/findings/{finding}/delete", UriKind.Relative),
            new { path = "../outside.bin", outputRoot = SyntheticDriverHost.RootName, sizeBytes = 10 });

        Assert.Equal(HttpStatusCode.BadRequest, traversal);
        Assert.Equal(HttpStatusCode.OK, carrying.StatusCode);
        Assert.False(File.Exists(leftover));
        Assert.True(File.Exists(precious));
        Assert.True(File.Exists(outside));
    }

    private static string Holding(SyntheticDriverHost driver, string path, int size)
    {
        string full = Path.Combine(driver.RecordingsDirectory, path);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[size]);

        return full;
    }

    private sealed class AppAgainstTheDriver : IAsyncDisposable
    {
        private readonly TestingWebApplicationFactory factory;

        private readonly WebApplicationFactory<Program> configured;

        public AppAgainstTheDriver(SyntheticDriverHost driver)
        {
            var settings = new IntegritySettings
            {
                OutputRoots =
                [
                    new StorageRootPath(new OutputRoot(SyntheticDriverHost.RootName), driver.RecordingsDirectory),
                ],
            };

            factory = new TestingWebApplicationFactory { DriverSocketPath = driver.SocketPath };
            configured = factory
                .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IHostedService>();
                    services.AddSingleton(settings);
                    services.AddSingleton<IRecordingFileSurvey>(
                        new LocalRecordingFileSurvey(settings, NullLogger<LocalRecordingFileSurvey>.Instance));
                    services.AddSingleton<IRecordingRepository>(new HeldInFlightRecordings());
                    services.AddScoped<IRecordingLedger>(_ => Ledger);
                    services.AddScoped<IEncodeWorkLedger>(_ => Working);
                    services.AddScoped<IIntegrityCheckRepository>(_ => Checks);
                }))
                .WithTestScheme();

            Client = configured.CreateClient();
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                TestAuthenticationHandler.SchemeName,
                "anything");
        }

        public HttpClient Client { get; }

        public HeldLedgerFiles Ledger { get; } = new();

        public HeldEncodeWorkFiles Working { get; } = new();

        public HeldIntegrityChecks Checks { get; } = new();

        public async Task<Guid> FoundAsync(string path)
        {
            (HttpStatusCode ran, _) = await PostAsync("/api/recordings/integrity/run");
            (_, JsonElement page) = await GetAsync("/api/recordings/integrity");

            Assert.Equal(HttpStatusCode.OK, ran);

            return page.GetProperty("data").GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("path").GetString() == path)
                .GetProperty("id")
                .GetGuid();
        }

        public async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
        {
            using HttpResponseMessage response = await Client.GetAsync(new Uri(path, UriKind.Relative));

            return await ReadAsync(response);
        }

        public async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string path)
        {
            using HttpResponseMessage response = await Client.PostAsJsonAsync(new Uri(path, UriKind.Relative), new { });

            return await ReadAsync(response);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await configured.DisposeAsync();
            await factory.DisposeAsync();
        }

        private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseMessage response)
        {
            string body = await response.Content.ReadAsStringAsync();

            if (!body.StartsWith('{'))
            {
                return (response.StatusCode, default);
            }

            using var document = JsonDocument.Parse(body);

            return (response.StatusCode, document.RootElement.Clone());
        }
    }

    [Fact]
    public async Task BrKd025CaptionsNoRecordingClaimsAreListedAsOrphansAndAreThrownAwayByThisProcess()
    {
        using var recordings = new RecordingStore();
        using var encodes = new RecordingStore();
        using var pictures = new RecordingStore();
        using var captions = new RecordingStore();
        recordings.Holding(KeptFile, 400);
        captions.Holding(Kept.Wire + ".captions", 30).Holding(Gone.Wire + ".captions", 20).Holding(Gone.Wire + ".captions.part", 5);
        await using IntegrityFeature feature = Walking(recordings, encodes, pictures, captions);

        (HttpStatusCode ran, _) = await feature.PostAsync("/api/recordings/integrity/run");
        (_, JsonElement page) = await feature.GetAsync("/api/recordings/integrity");

        Assert.Equal(HttpStatusCode.OK, ran);
        Assert.Equal(
            [
                $"captions/{Gone.Wire}.captions noLedgerRow",
                $"captions/{Gone.Wire}.captions.part noLedgerRow",
            ],
            Listed(page));

        IntegrityFinding finding = feature.Checks.Saved[^1].Findings.Single(found => found.Path == Gone.Wire + ".captions");
        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"/api/recordings/integrity/findings/{finding.Id.Value}/delete");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("captions", body.GetProperty("data").GetProperty("outputRoot").GetString());
        Assert.False(File.Exists(Path.Combine(captions.Root, Gone.Wire + ".captions")));
        Assert.True(File.Exists(Path.Combine(captions.Root, Kept.Wire + ".captions")));
        Assert.Empty(feature.Driver.AskedAboutStrays);
    }

    [Fact]
    public async Task APictureNothingClaimsIsThrownAwayByThisProcessAndNothingBesideItIsTouched()
    {
        using var recordings = new RecordingStore();
        using var encodes = new RecordingStore();
        using var pictures = new RecordingStore();
        recordings.Holding(KeptFile, 400);
        pictures.Holding(Kept.Wire + ".jpg", 30).Holding(Gone.Wire + ".jpg", 20);
        await using IntegrityFeature feature = Walking(recordings, encodes, pictures);
        IReadOnlyList<string> recordingsBefore = recordings.Fingerprint();
        IntegrityFinding finding = await CheckedAsync(feature, Gone.Wire + ".jpg");

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"/api/recordings/integrity/findings/{finding.Id.Value}/delete");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("data").GetProperty("fileRemoved").GetBoolean());
        Assert.Equal("thumbnails", body.GetProperty("data").GetProperty("outputRoot").GetString());
        Assert.False(File.Exists(Path.Combine(pictures.Root, Gone.Wire + ".jpg")));
        Assert.True(File.Exists(Path.Combine(pictures.Root, Kept.Wire + ".jpg")));
        Assert.Equal(recordingsBefore, recordings.Fingerprint());
        Assert.Empty(feature.Driver.AskedAboutStrays);
    }

    [Fact]
    public async Task AnArtefactThatChangedSinceTheCheckIsRefusedAndLeftAsItNowIs()
    {
        using var recordings = new RecordingStore();
        using var encodes = new RecordingStore();
        using var pictures = new RecordingStore();
        recordings.Holding(KeptFile, 400);
        encodes.Holding(GoneArtefact, 80);
        await using IntegrityFeature feature = Walking(recordings, encodes, pictures);
        IntegrityFinding finding = await CheckedAsync(feature, GoneArtefact);
        await File.AppendAllTextAsync(Path.Combine(encodes.Root, GoneArtefact), "more");

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"/api/recordings/integrity/findings/{finding.Id.Value}/delete");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("fileChanged", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Equal(84, new FileInfo(Path.Combine(encodes.Root, GoneArtefact)).Length);
    }

    [Fact]
    public async Task APictureWhoseRecordingIsWrittenDownSinceTheCheckIsRefusedAndKept()
    {
        using var recordings = new RecordingStore();
        using var encodes = new RecordingStore();
        using var pictures = new RecordingStore();
        recordings.Holding(KeptFile, 400);
        pictures.Holding(Gone.Wire + ".jpg", 20);
        await using IntegrityFeature feature = Walking(recordings, encodes, pictures);
        IntegrityFinding finding = await CheckedAsync(feature, Gone.Wire + ".jpg");
        feature.Ledger.Rows.Add(
            LedgerFile.StillWriting(Gone, IntegrityFeature.Primary, new RecordingFileName(Gone.Wire + ".ts")));

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"/api/recordings/integrity/findings/{finding.Id.Value}/delete");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("fileChanged", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.True(File.Exists(Path.Combine(pictures.Root, Gone.Wire + ".jpg")));
    }

    [Fact]
    public async Task APictureInTheEncodeRootWhoseRecordingIsWrittenDownSinceTheCheckCannotBeThrownAway()
    {
        using var recordings = new RecordingStore();
        using var shared = new RecordingStore();
        recordings.Holding(KeptFile, 400);
        shared.Holding(Gone.Wire + ".jpg", 20);
        await using IntegrityFeature feature = Walking(recordings, shared, shared);
        IntegrityFinding finding = await CheckedAsync(feature, Gone.Wire + ".jpg");
        feature.Ledger.Rows.Add(
            LedgerFile.StillWriting(Gone, IntegrityFeature.Primary, new RecordingFileName(Gone.Wire + ".ts")));

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"/api/recordings/integrity/findings/{finding.Id.Value}/delete");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("fileChanged", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.True(File.Exists(Path.Combine(shared.Root, Gone.Wire + ".jpg")));
    }

    private static IntegrityFeature Walking(
        RecordingStore recordings,
        RecordingStore encodes,
        RecordingStore pictures,
        RecordingStore? captions = null)
    {
        var feature = new IntegrityFeature(
            walking: recordings.Root,
            encoding: new EncodeSettings { OutputRoots = [new StorageRootPath(Encodes, encodes.Root)] },
            drawing: pictures.Root,
            captioning: captions?.Root);

        feature.Ledger.Rows.Add(LedgerFile.Ended(
            Kept,
            IntegrityFeature.Primary,
            new RecordingFileName(KeptFile),
            LedgerClaim.EverythingLanded,
            400));
        feature.Working.Files.Add(new DeclaredFile(Encodes, KeptArtefact));

        return feature;
    }

    private static string[] Listed(JsonElement page)
        => [.. page.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(item => $"{item.GetProperty("outputRoot").GetString()}/{item.GetProperty("path").GetString()} "
                            + item.GetProperty("fault").GetString())
            .Order(StringComparer.Ordinal)];
}
