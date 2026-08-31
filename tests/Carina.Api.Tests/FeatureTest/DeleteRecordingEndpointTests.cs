using System.Net;
using System.Text;
using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Recordings;
using Carina.Infrastructure.Thumbnails;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class DeleteRecordingEndpointTests
{
    private const string RecordingIdTextDescription =
        "A recording is named by the thirty-two hexadecimal digits the ledger holds, without separators.";

    [Fact]
    public async Task ARecordingThatHasEndedIsThrownAwayAndIsGoneFromTheLedger()
    {
        await using var feature = new RecordingFeature();
        Recording held = Ended(feature);
        feature.Eraser.Answer = RecordingErasure.Erased(2);

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/recordings/{held.Id.Wire}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("status").GetBoolean());
        Assert.Equal(held.Id.Wire, body.GetProperty("data").GetProperty("recordingId").GetString());
        Assert.Equal(2, body.GetProperty("data").GetProperty("filesRemoved").GetInt32());
        Assert.Equal([held.Id], feature.Eraser.Asked);
        Assert.Empty(feature.Recordings.Recordings);

        (HttpStatusCode after, _) = await feature.GetAsync($"/api/recordings/{held.Id.Wire}");

        Assert.Equal(HttpStatusCode.NotFound, after);
    }

    [Fact]
    public async Task ARecordingStillBeingWrittenIsRefusedAndNothingOfItIsTouched()
    {
        await using var feature = new RecordingFeature();
        Recording writing = feature.Held();

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/recordings/{writing.Id.Wire}");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Equal("stillRecording", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Empty(feature.Eraser.Asked);
        Assert.Single(feature.Recordings.Recordings);
    }

    [Fact]
    public async Task ARecordingNobodyEverWroteDownIsAnAbsenceRatherThanARefusal()
    {
        await using var feature = new RecordingFeature();

        (HttpStatusCode status, JsonElement body) =
            await feature.DeleteAsync($"/api/recordings/{RecordingId.New().Wire}");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("noSuchRecording", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Empty(feature.Eraser.Asked);
    }

    [Theory]
    [InlineData("not-a-recording")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("00000000000000000000000000000000")]
    public async Task SomethingThatIsNotARecordingIdIsRefusedBeforeAnythingIsRead(string id)
    {
        await using var feature = new RecordingFeature();
        feature.Held();

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/recordings/{id}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(RecordingIdTextDescription, body.GetProperty("message").GetString());
        Assert.DoesNotContain(id, body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Empty(feature.Eraser.Asked);
        Assert.Single(feature.Recordings.Recordings);
    }

    [Fact]
    public async Task ARootThatCannotBeReachedRefusesTheDeletionAndLeavesTheRowWhereItIs()
    {
        await using var feature = new RecordingFeature();
        Recording held = Ended(feature);
        feature.Eraser.Answer = RecordingErasure.Refused(ErasureFault.RootOutOfReach, "the mount has gone");

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/recordings/{held.Id.Wire}");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("rootOutOfReach", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Single(feature.Recordings.Recordings);

        (HttpStatusCode after, _) = await feature.GetAsync($"/api/recordings/{held.Id.Wire}");

        Assert.Equal(HttpStatusCode.OK, after);
    }

    [Fact]
    public async Task AFileLeftBehindKeepsTheRowBecauseTheRowIsWhatSaysTheJobIsUnfinished()
    {
        await using var feature = new RecordingFeature();
        Recording held = Ended(feature);
        feature.Eraser.Answer = RecordingErasure.Refused(ErasureFault.FileLeftBehind, "permission denied");

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/recordings/{held.Id.Wire}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("filesLeftBehind", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Single(feature.Recordings.Recordings);

        feature.Eraser.Answer = RecordingErasure.Erased(1);

        (HttpStatusCode again, _) = await feature.DeleteAsync($"/api/recordings/{held.Id.Wire}");

        Assert.Equal(HttpStatusCode.OK, again);
        Assert.Empty(feature.Recordings.Recordings);
    }

    [Fact]
    public async Task OnlyOneDeletionRunsAtATimeAndTheOneWaitingIsToldWhichIsUnderway()
    {
        await using var feature = new RecordingFeature();
        Recording first = Ended(feature);
        Recording second = Ended(feature);
        using var reached = new SemaphoreSlim(0);
        using var release = new SemaphoreSlim(0);

        feature.Eraser.WhenErasing = () =>
        {
            reached.Release();
            release.Wait(TimeSpan.FromSeconds(30));
        };

        Task<(HttpStatusCode Status, JsonElement Body)> running =
            feature.DeleteAsync($"/api/recordings/{first.Id.Wire}");

        Assert.True(await reached.WaitAsync(TimeSpan.FromSeconds(30)));

        feature.Eraser.WhenErasing = null;

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/recordings/{second.Id.Wire}");

        release.Release();

        Assert.Equal(HttpStatusCode.OK, (await running).Status);
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("oneIsAlreadyBeingDiscarded", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Contains(first.Id.Wire, body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal([first.Id], feature.Eraser.Asked);
    }

    [Fact]
    public async Task ADeletionThatFinishedLetsTheNextOneRun()
    {
        await using var feature = new RecordingFeature();
        Recording first = Ended(feature);
        Recording second = Ended(feature);

        Assert.Equal(HttpStatusCode.OK, (await feature.DeleteAsync($"/api/recordings/{first.Id.Wire}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await feature.DeleteAsync($"/api/recordings/{second.Id.Wire}")).Status);
        Assert.Equal([first.Id, second.Id], feature.Eraser.Asked);
    }

    [Fact]
    public async Task ARefusedDeletionStillLetsTheNextOneRun()
    {
        await using var feature = new RecordingFeature();
        Recording first = Ended(feature);
        Recording second = Ended(feature);
        feature.Eraser.Answer = RecordingErasure.Refused(ErasureFault.FileLeftBehind, "permission denied");

        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            (await feature.DeleteAsync($"/api/recordings/{first.Id.Wire}")).Status);

        feature.Eraser.Answer = RecordingErasure.Erased(1);

        Assert.Equal(HttpStatusCode.OK, (await feature.DeleteAsync($"/api/recordings/{second.Id.Wire}")).Status);
    }

    [Fact]
    public async Task TheFileAndThePictureBothLeaveTheDiskAndTheRowGoesLast()
    {
        using var disk = new ErasableDisk();
        await using var feature = new RecordingFeature(disk.Eraser);
        Recording held = Ended(feature);
        disk.Holding(held);
        disk.Holding(RecordingId.New());

        (HttpStatusCode status, _) = await feature.DeleteAsync($"/api/recordings/{held.Id.Wire}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(File.Exists(disk.RecordingAt(held.Id)));
        Assert.False(File.Exists(disk.PictureAt(held.Id)));
        Assert.Empty(feature.Recordings.Recordings);
    }

    [Fact]
    public async Task ARecordingStillBeingWrittenKeepsItsFileOnTheDisk()
    {
        using var disk = new ErasableDisk();
        await using var feature = new RecordingFeature(disk.Eraser);
        Recording writing = feature.Held();
        disk.Holding(writing);

        (HttpStatusCode status, _) = await feature.DeleteAsync($"/api/recordings/{writing.Id.Wire}");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.True(File.Exists(disk.RecordingAt(writing.Id)));
        Assert.True(File.Exists(disk.PictureAt(writing.Id)));
        Assert.Single(feature.Recordings.Recordings);
    }

    [Fact]
    public async Task ARootHoldingNoFileAtAllIsTakenForALostMountAndTheDeletionIsRefused()
    {
        using var disk = new ErasableDisk();
        await using var feature = new RecordingFeature(disk.Eraser);
        Recording held = Ended(feature);

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/recordings/{held.Id.Wire}");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("rootOutOfReach", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Single(feature.Recordings.Recordings);
    }

    [Fact]
    public async Task ADeleteCarryingNoBodyReachesTheEndpointWithoutNamingAContentType()
    {
        await using var feature = new RecordingFeature();
        Recording held = Ended(feature);

        using var asking = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri($"/api/recordings/{held.Id.Wire}", UriKind.Relative));
        using HttpResponseMessage response = await feature.Client.SendAsync(asking);

        Assert.Null(asking.Content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ADeleteThatNamesNoOriginIsRefusedBecauseThatIsWhatStandsInForTheContentType()
    {
        await using var feature = new RecordingFeature();
        Recording held = Ended(feature);
        feature.Client.DefaultRequestHeaders.Remove(HeaderNames.Origin);

        using var asking = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri($"/api/recordings/{held.Id.Wire}", UriKind.Relative));
        using HttpResponseMessage response = await feature.Client.SendAsync(asking);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(feature.Eraser.Asked);
        Assert.Single(feature.Recordings.Recordings);
    }

    [Fact]
    public async Task ADeleteCarryingABodyThatIsNotJsonIsStillRefused()
    {
        await using var feature = new RecordingFeature();
        Recording held = Ended(feature);

        using var asking = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri($"/api/recordings/{held.Id.Wire}", UriKind.Relative))
        {
            Content = new StringContent("anything=1", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        using HttpResponseMessage response = await feature.Client.SendAsync(asking);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(feature.Eraser.Asked);
    }

    [Fact]
    public async Task ADriverThatDidNotAnswerIsNotADriverThatRefused()
    {
        using var disk = new ErasableDisk();
        await using var feature = new RecordingFeature(disk.Eraser);
        Recording held = Ended(feature);
        disk.Holding(held);
        disk.Driver.StandingInForTheDriver = null;
        disk.Driver.Answer = DriverCall<RecordingErasedDto>.Unreachable("the socket was not there");

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/recordings/{held.Id.Wire}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("driverUnreachable", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.True(File.Exists(disk.RecordingAt(held.Id)));
        Assert.True(File.Exists(disk.PictureAt(held.Id)));
        Assert.Single(feature.Recordings.Recordings);
    }

    [Fact]
    public async Task ADriverThatRefusedIsNotADriverThatDidNotAnswer()
    {
        using var disk = new ErasableDisk();
        await using var feature = new RecordingFeature(disk.Eraser);
        Recording held = Ended(feature);
        disk.Holding(held);
        disk.Driver.StandingInForTheDriver = null;
        disk.Driver.Answer = DriverCall<RecordingErasedDto>.Refused(
            new DriverProblem(SessionRefusalTitles.CapabilityMissing, ["it declares no such thing"]));

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/recordings/{held.Id.Wire}");

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Equal("driverRefused", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.True(File.Exists(disk.RecordingAt(held.Id)));
        Assert.Single(feature.Recordings.Recordings);
    }

    private static Recording Ended(RecordingFeature feature)
    {
        Recording held = feature.Held();

        held.Abort(RecordingFeature.Noon.AddMinutes(20));
        held.Settle(RecordingOutcome.Complete, 1_000_000, RecordingFeature.Noon.AddMinutes(20));

        return held;
    }

    private sealed class ErasableDisk : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("carina-delete-").FullName;

        private readonly string gallery = Directory.CreateTempSubdirectory("carina-delete-pictures-").FullName;

        public ErasableDisk()
        {
            Driver = new ErasingDriverClient { StandingInForTheDriver = TakeItOffTheDisk };
            Eraser = new DriverRecordingFileEraser(
                Driver,
                new ThumbnailSettings { WrittenTo = gallery },
                NullLogger<DriverRecordingFileEraser>.Instance);
        }

        public ErasingDriverClient Driver { get; }

        public IRecordingFileEraser Eraser { get; }

        public string RecordingAt(RecordingId id) => Path.Combine(root, RecordingFile.Of(id.Wire));

        private DriverCall<RecordingErasedDto> TakeItOffTheDisk(string recordingId, string outputRoot)
        {
            if (Directory.GetFiles(root).Length is 0)
            {
                return DriverCall<RecordingErasedDto>.Refused(
                    new DriverProblem(
                        SessionRefusalTitles.OutputUnavailable,
                        ["the root holds no file at all, which is what a lost mount looks like"]));
            }

            string held = Path.Combine(root, RecordingFile.Of(recordingId));
            bool wasThere = File.Exists(held);

            File.Delete(held);

            return DriverCall<RecordingErasedDto>.Reached(
                new RecordingErasedDto { RecordingId = recordingId, FileRemoved = wasThere });
        }

        public string PictureAt(RecordingId id) => Path.Combine(gallery, id.Wire + ThumbnailJob.Extension);

        public void Holding(Recording recording) => Holding(recording.Id);

        public void Holding(RecordingId id)
        {
            File.WriteAllBytes(RecordingAt(id), new byte[188]);
            File.WriteAllBytes(PictureAt(id), new byte[16]);
        }

        public void Dispose()
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(gallery, recursive: true);
        }
    }
}
