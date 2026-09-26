using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

public sealed class DeleteIntegrityFindingEndpointTests
{
    private const string IdDescription =
        "A finding is named by the identifier the most recent check gave it, written as a GUID.";

    private static readonly RecordingId Recording = new(new Guid("7a3e5c1b-0000-0000-0000-00000000000c"));

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
}
