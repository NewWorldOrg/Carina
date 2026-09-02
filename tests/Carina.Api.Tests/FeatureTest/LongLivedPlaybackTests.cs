using System.Net;
using System.Net.Http.Headers;

using Carina.Domain.Auth;
using Carina.Domain.Channels;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class GatedStream(byte[] bytes) : Stream
{
    private readonly SemaphoreSlim released = new(0);

    private long position;

    private int reach;

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => bytes.Length;

    public override long Position
    {
        get => position;
        set => position = value;
    }

    public void Release(int upTo)
    {
        Volatile.Write(ref reach, upTo);
        released.Release();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (position >= Volatile.Read(ref reach) && position < bytes.Length)
        {
            await released.WaitAsync(cancellationToken);
        }

        int take = (int)Math.Min(Math.Min(buffer.Length, Volatile.Read(ref reach) - position), bytes.Length - position);

        if (take <= 0)
        {
            return 0;
        }

        bytes.AsMemory((int)position, take).CopyTo(buffer);
        position += take;

        return take;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override long Seek(long offset, SeekOrigin origin)
    {
        position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            _ => bytes.Length + offset,
        };

        return position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            released.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class GatedPlaybackFiles(byte[] bytes) : IPlaybackFileStore
{
    public GatedStream Handed { get; } = new(bytes);

    public PlaybackFile? Find(OutputRoot root, RecordingFileName fileName) => new(root, fileName, bytes.Length);

    public Stream? OpenRead(PlaybackFile file) => Handed;

    public StreamSource? SourceOf(PlaybackFile file) => null;
}

internal sealed class GatedViewing(GatedStream output) : IOnTheFlyViewing
{
    public OnTheFlyStanding Standing { get; } = new(
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(1),
        LiveProfile.Hd30,
        LiveEncoderChoice.Asked(LiveEncoder.Software),
        attributesWereMeasured: true,
        1,
        2);

    public Stream Output => output;

    public Task<TranscoderExit> Completion { get; } = Task.FromResult(TranscoderExit.Finished());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class GatedOnTheFlyPlayer(byte[] bytes) : IOnTheFlyPlayer
{
    public GatedStream Handed { get; } = new(bytes);

    public Task<OnTheFlyStart> StartAsync(
        PlaybackFile file,
        ServiceId service,
        TimeSpan from,
        LiveProfile profile,
        CancellationToken cancellationToken)
        => Task.FromResult(OnTheFlyStart.Started(new GatedViewing(Handed)));
}

[Collection(FeatureTestCollection.Name)]
public sealed class LongLivedPlaybackTests
{
    private const int Size = 256 * 1024;

    private const int FirstHalf = Size / 2;

    private static readonly byte[] Written = [.. Enumerable.Range(0, Size).Select(index => (byte)(index % 251))];

    [Fact]
    public async Task ARecordingAlreadyBeingHandedOverKeepsFlowingAfterItsSessionIsEnded()
    {
        GatedPlaybackFiles files = new(Written);
        await using AuthProbe probe = Wiring(files, new GatedOnTheFlyPlayer(Written), out Recording recording);
        AuthSession session = await probe.SignedInAsync();

        files.Handed.Release(FirstHalf);

        using HttpResponseMessage response = await probe.Client.GetAsync(
            Bytes(recording),
            HttpCompletionOption.ResponseHeadersRead);
        await using Stream body = await response.Content.ReadAsStreamAsync();
        byte[] heard = new byte[Size];

        await body.ReadExactlyAsync(heard.AsMemory(0, FirstHalf));

        session.Revoke(DateTime.UtcNow);
        files.Handed.Release(Size);

        await body.ReadExactlyAsync(heard.AsMemory(FirstHalf));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Written, heard);
    }

    [Fact]
    public async Task TheNextRequestForTheRecordingAfterThatSessionEndedIsRefused()
    {
        await using AuthProbe probe = Wiring(new GatedPlaybackFiles(Written), new GatedOnTheFlyPlayer(Written), out Recording recording);
        AuthSession session = await probe.SignedInAsync();

        session.Revoke(DateTime.UtcNow);

        using HttpResponseMessage response = await probe.Client.GetAsync(Bytes(recording), HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task APictureAlreadyBeingTranscodedKeepsFlowingAfterItsSessionIsEnded()
    {
        GatedOnTheFlyPlayer player = new(Written);
        await using AuthProbe probe = Wiring(new GatedPlaybackFiles(Written), player, out Recording recording);
        AuthSession session = await probe.SignedInAsync();

        player.Handed.Release(FirstHalf);

        using HttpRequestMessage asking = Playing(recording);
        using HttpResponseMessage response = await probe.Client.SendAsync(asking, HttpCompletionOption.ResponseHeadersRead);
        await using Stream body = await response.Content.ReadAsStreamAsync();
        byte[] heard = new byte[Size];

        await body.ReadExactlyAsync(heard.AsMemory(0, FirstHalf));

        session.Revoke(DateTime.UtcNow);
        player.Handed.Release(Size);

        await body.ReadExactlyAsync(heard.AsMemory(FirstHalf));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Written, heard);
    }

    [Fact]
    public async Task TheNextPictureAskedForAfterThatSessionEndedIsRefused()
    {
        await using AuthProbe probe = Wiring(new GatedPlaybackFiles(Written), new GatedOnTheFlyPlayer(Written), out Recording recording);
        AuthSession session = await probe.SignedInAsync();

        session.Revoke(DateTime.UtcNow);

        using HttpRequestMessage asking = Playing(recording);
        using HttpResponseMessage response = await probe.Client.SendAsync(asking, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    private static Uri Bytes(Recording recording) => new($"/api/videos/{recording.Id.Wire}", UriKind.Relative);

    private static HttpRequestMessage Playing(Recording recording)
    {
        HttpRequestMessage asking = new(HttpMethod.Get, new Uri($"/api/videos/{recording.Id.Wire}/play", UriKind.Relative));

        asking.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        return asking;
    }

    private static AuthProbe Wiring(IPlaybackFileStore files, IOnTheFlyPlayer player, out Recording recording)
    {
        HeldRecordings recordings = new();

        recording = RecordingFeature.Begin(RecordingId.New());
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(RecordingFeature.Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, Size, RecordingFeature.Noon.AddMinutes(30));
        recordings.Recordings.Add(recording);

        return AuthProbe.OverHttp(services =>
        {
            services.RemoveAll<IPlaybackFileStore>();
            services.RemoveAll<IOnTheFlyPlayer>();
            services.AddSingleton<IRecordingDirectory>(recordings);
            services.AddSingleton(files);
            services.AddSingleton(player);
        });
    }
}
