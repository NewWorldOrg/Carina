using System.Net;
using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class StorageEndpointTests
{
    [Fact]
    public async Task EachRootTheDriverDeclaresIsAnsweredWithTheRoomItHas()
    {
        await using var feature = new IntegrityFeature();
        feature.Driver.Roots.Add(Root("primary", free: 900, total: 1_000, writable: true));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/storage");
        JsonElement root = Assert.Single(body.GetProperty("data").GetProperty("roots").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("primary", root.GetProperty("name").GetString());
        Assert.Equal(900, root.GetProperty("freeBytes").GetInt64());
        Assert.Equal(1_000, root.GetProperty("totalBytes").GetInt64());
        Assert.True(root.GetProperty("writable").GetBoolean());
        Assert.Equal(0, root.GetProperty("committedBytes").GetInt64());
        Assert.Equal(0, root.GetProperty("recordingsInFlight").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("shortfall").ValueKind);
    }

    [Fact(DisplayName = "BR-EV-001: a root this process holds for encoding is answered after the driver's, measured here")]
    public async Task ARootThisProcessHoldsForEncodingIsAnsweredAfterTheDrivers()
    {
        using var shelf = new RecordingStore();
        await using var feature = new IntegrityFeature(encoding: new EncodeSettings
        {
            OutputRoots = [new StorageRootPath(new OutputRoot("encodes"), shelf.Root)],
        });
        feature.Driver.Roots.Add(Root("primary", free: 900, total: 1_000, writable: true));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/storage");
        JsonElement[] roots = [.. body.GetProperty("data").GetProperty("roots").EnumerateArray()];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["primary", "encodes"], roots.Select(root => root.GetProperty("name").GetString()!).ToArray());
        Assert.True(roots[1].GetProperty("writable").GetBoolean());
        Assert.True(roots[1].GetProperty("totalBytes").GetInt64() > 0);
        Assert.Equal(0, roots[1].GetProperty("recordingsInFlight").GetInt32());
        Assert.Equal(JsonValueKind.Null, roots[1].GetProperty("shortfall").ValueKind);
        Assert.DoesNotContain(shelf.Root, body.GetRawText(), StringComparison.Ordinal);
        Assert.Empty(shelf.Fingerprint());
    }

    [Fact(DisplayName = "BR-EV-001: a held root named like one the driver declares is the driver's in the answer")]
    public async Task AHeldRootNamedLikeOneTheDriverDeclaresIsTheDriversInTheAnswer()
    {
        using var shelf = new RecordingStore();
        await using var feature = new IntegrityFeature(encoding: new EncodeSettings
        {
            OutputRoots = [new StorageRootPath(IntegrityFeature.Primary, shelf.Root)],
        });
        feature.Driver.Roots.Add(Root("primary", free: 900, total: 1_000, writable: false));

        (_, JsonElement body) = await feature.GetAsync("/api/storage");
        JsonElement root = Assert.Single(body.GetProperty("data").GetProperty("roots").EnumerateArray());

        Assert.Equal(900, root.GetProperty("freeBytes").GetInt64());
        Assert.False(root.GetProperty("writable").GetBoolean());
    }

    [Fact]
    public async Task ARootSaysNothingAboutWhereOnTheHostItIs()
    {
        await using var feature = new IntegrityFeature(new IntegritySettings
        {
            OutputRoots = [new StorageRootPath(IntegrityFeature.Primary, "/srv/recordings-of-this-host")],
        });
        feature.Driver.Roots.Add(Root("primary", free: 900, total: 1_000, writable: true));

        (_, JsonElement body) = await feature.GetAsync("/api/storage");
        JsonElement root = Assert.Single(body.GetProperty("data").GetProperty("roots").EnumerateArray());

        Assert.Equal(
            ["name", "freeBytes", "totalBytes", "writable", "committedBytes", "recordingsInFlight", "shortfall"],
            root.EnumerateObject().Select(field => field.Name).ToArray());
        Assert.DoesNotContain(
            "/srv/recordings-of-this-host",
            body.GetRawText(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhatIsStillToBeWrittenByTheRecordingsInFlightIsCountedAgainstTheirOwnRoot()
    {
        await using var feature = new IntegrityFeature();
        feature.Driver.Roots.Add(Root("primary", free: long.MaxValue, total: long.MaxValue, writable: true));
        feature.Driver.Roots.Add(Root("bulk", free: long.MaxValue, total: long.MaxValue, writable: true));
        feature.Running.InFlight.Add(IntegrityFeature.Writing(
            IntegrityFeature.Primary,
            IntegrityFeature.Noon,
            IntegrityFeature.Noon.AddHours(2)));
        feature.Running.InFlight.Add(IntegrityFeature.Writing(
            IntegrityFeature.Primary,
            IntegrityFeature.Noon,
            IntegrityFeature.Noon.AddHours(1),
            eventId: 4002));

        (_, JsonElement body) = await feature.GetAsync("/api/storage");
        JsonElement[] roots = [.. body.GetProperty("data").GetProperty("roots").EnumerateArray()];

        Assert.Equal(22_275_000_000, roots[0].GetProperty("committedBytes").GetInt64());
        Assert.Equal(2, roots[0].GetProperty("recordingsInFlight").GetInt32());
        Assert.Equal(0, roots[1].GetProperty("committedBytes").GetInt64());
        Assert.Equal(0, roots[1].GetProperty("recordingsInFlight").GetInt32());
    }

    [Fact]
    public async Task OnlyTheRestOfAWindowIsCountedAgainstTheRoom()
    {
        await using var feature = new IntegrityFeature();
        feature.Driver.Roots.Add(Root("primary", free: long.MaxValue, total: long.MaxValue, writable: true));
        feature.Running.InFlight.Add(IntegrityFeature.Writing(
            IntegrityFeature.Primary,
            IntegrityFeature.Noon.AddMinutes(-30),
            IntegrityFeature.Noon.AddMinutes(30)));

        (_, JsonElement body) = await feature.GetAsync("/api/storage");
        JsonElement root = Assert.Single(body.GetProperty("data").GetProperty("roots").EnumerateArray());

        Assert.Equal(3_712_500_000, root.GetProperty("committedBytes").GetInt64());
    }

    [Fact]
    public async Task AWindowOfThreeHoursIsSpokenForByThreeTimesWhatAnHourIs()
    {
        await using var feature = new IntegrityFeature();
        feature.Driver.Roots.Add(Root("primary", free: long.MaxValue, total: long.MaxValue, writable: true));
        feature.Running.InFlight.Add(IntegrityFeature.Writing(
            IntegrityFeature.Primary,
            IntegrityFeature.Noon,
            IntegrityFeature.Noon.AddHours(3)));

        (_, JsonElement body) = await feature.GetAsync("/api/storage");
        JsonElement root = Assert.Single(body.GetProperty("data").GetProperty("roots").EnumerateArray());

        Assert.Equal(22_275_000_000, root.GetProperty("committedBytes").GetInt64());
    }

    [Fact]
    public async Task ARootThatCouldNotBeMeasuredIsToldApartFromOneWithNoRoomLeft()
    {
        await using var feature = new IntegrityFeature();
        feature.Driver.Roots.Add(Root("unreachable", free: 0, total: 0, writable: false));
        feature.Driver.Roots.Add(Root("full", free: 0, total: 1_000, writable: true));

        (_, JsonElement body) = await feature.GetAsync("/api/storage");
        JsonElement[] roots = [.. body.GetProperty("data").GetProperty("roots").EnumerateArray()];

        Assert.Equal("rootUnmeasured", roots[0].GetProperty("shortfall").GetString());
        Assert.Equal("noRoomLeft", roots[1].GetProperty("shortfall").GetString());
    }

    [Fact]
    public async Task ARootTheDriverWillNotWriteToSaysSo()
    {
        await using var feature = new IntegrityFeature();
        feature.Driver.Roots.Add(Root("primary", free: 900, total: 1_000, writable: false));

        (_, JsonElement body) = await feature.GetAsync("/api/storage");
        JsonElement root = Assert.Single(body.GetProperty("data").GetProperty("roots").EnumerateArray());

        Assert.Equal("rootNotWritable", root.GetProperty("shortfall").GetString());
    }

    [Fact]
    public async Task ARootWithLessRoomThanIsSpokenForSaysSo()
    {
        await using var feature = new IntegrityFeature();
        feature.Driver.Roots.Add(Root("primary", free: 1_000, total: 100_000_000_000, writable: true));
        feature.Running.InFlight.Add(IntegrityFeature.Writing(
            IntegrityFeature.Primary,
            IntegrityFeature.Noon,
            IntegrityFeature.Noon.AddHours(1)));

        (_, JsonElement body) = await feature.GetAsync("/api/storage");
        JsonElement root = Assert.Single(body.GetProperty("data").GetProperty("roots").EnumerateArray());

        Assert.Equal("shortOfTheEstimate", root.GetProperty("shortfall").GetString());
    }

    [Fact]
    public async Task ARootBeingWrittenToThatTheDriverNeverDeclaredIsAnsweredRatherThanLeftOut()
    {
        await using var feature = new IntegrityFeature();
        feature.Driver.Roots.Add(Root("primary", free: long.MaxValue, total: long.MaxValue, writable: true));
        feature.Running.InFlight.Add(IntegrityFeature.Writing(
            IntegrityFeature.Bulk,
            IntegrityFeature.Noon,
            IntegrityFeature.Noon.AddHours(1)));

        (_, JsonElement body) = await feature.GetAsync("/api/storage");
        JsonElement[] roots = [.. body.GetProperty("data").GetProperty("roots").EnumerateArray()];

        Assert.Equal(["primary", "bulk"], roots.Select(root => root.GetProperty("name").GetString()!).ToArray());
        Assert.Equal("rootUndeclared", roots[1].GetProperty("shortfall").GetString());
        Assert.Equal(1, roots[1].GetProperty("recordingsInFlight").GetInt32());
        Assert.Equal(7_425_000_000, roots[1].GetProperty("committedBytes").GetInt64());
    }

    [Fact]
    public async Task ADriverThatCannotBeReachedIsSaidToBeUnreachableRatherThanAnsweredAsAnEmptyDisk()
    {
        await using var feature = new IntegrityFeature();
        feature.Driver.Unreachable = "no socket at that path";

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/storage");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Equal("no socket at that path", body.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task ADriverThatRefusesIsToldApartFromOneThatCannotBeReached()
    {
        await using var feature = new IntegrityFeature();
        feature.Driver.Refuses = new DriverProblem("storage is not offered", ["no roots are configured"]);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/storage");

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Equal(
            "storage is not offered: no roots are configured",
            body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ADriverThatDeclaresNoRootAnswersAnEmptyListRatherThanAFailure()
    {
        await using var feature = new IntegrityFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/storage");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(body.GetProperty("data").GetProperty("roots").EnumerateArray());
    }

    [Fact]
    public async Task AskingTwiceInQuickSuccessionDoesNotMakeTheDriverWriteTwice()
    {
        await using var feature = new IntegrityFeature();
        feature.Driver.Roots.Add(Root("primary", free: 900, total: 1_000, writable: true));

        await feature.GetAsync("/api/storage");
        await feature.GetAsync("/api/storage");

        Assert.Equal(1, feature.Driver.Reads);

        feature.Clock.Now = IntegrityFeature.Noon.AddMinutes(2);
        await feature.GetAsync("/api/storage");

        Assert.Equal(2, feature.Driver.Reads);
    }

    private static StorageRootDto Root(string name, long free, long total, bool writable)
        => new() { Name = name, FreeBytes = free, TotalBytes = total, Writable = writable };
}
