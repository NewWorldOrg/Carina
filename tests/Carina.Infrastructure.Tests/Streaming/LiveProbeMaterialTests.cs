using System.Diagnostics;
using System.Runtime.Versioning;

using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Machines;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

/// <summary>
/// Feeds a synthetic broadcast into the live transcoder at the rate it was written for, joining it part way
/// through a group of pictures, and times how long the transcoder reads before it decodes. Within ffmpeg's own
/// limits of five megabytes and five seconds, the same broadcast is read for five seconds.
/// The superimposition is left out: written here as a stream ffmpeg has to guess the format of, it holds every
/// packet back until a whole probe's worth has arrived, which a slow single-service file stretches out of all
/// proportion to a multiplex.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class LiveProbeMaterialTests : IDisposable
{
    private const int PacketLength = 188;

    private const int PacketsAtATime = 40;

    private static readonly TimeSpan Length = TimeSpan.FromSeconds(8);

    private static readonly TimeSpan JoinedPartWay = TimeSpan.FromSeconds(2.3);

    private static readonly TimeSpan LongestProbe = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan LongestToTheFirstPicture = TimeSpan.FromSeconds(2);

    private static readonly ServiceId Service = new(SyntheticBroadcast.SomeProgramNumber);

    private readonly string room = Directory.CreateTempSubdirectory("carina-probe").FullName;

    private readonly TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 1 });

    public void Dispose() => Directory.Delete(room, recursive: true);

    public static TheoryData<string> Broadcasts => ["measured", "standard definition", "full hd", "dual mono"];

    [Theory]
    [MemberData(nameof(Broadcasts))]
    public async Task TheLiveTranscoderHasReadWhatItsInputCarriesAndWrittenItsFirstPictureSoonAfterTheBroadcastArrives(string broadcast)
    {
        SyntheticBroadcast written = Of(broadcast) with { Length = Length, WithSuperimpose = false };
        byte[] bytes = await File.ReadAllBytesAsync(await written.WriteAsync(Path.Combine(room, "live.m2ts")));
        double bytesPerSecond = bytes.Length / Length.TotalSeconds;
        int joined = (int)(JoinedPartWay.TotalSeconds * bytesPerSecond) / PacketLength * PacketLength;

        LiveTranscoderStart start = await Factory().StartAsync(
            Service,
            LiveProfile.Hd30,
            SoundPlacement.WholeStream(0),
            StreamAttributes.SafeSide,
            CaptionOutlet.Drawn,
            CancellationToken.None);

        Assert.True(start.Running, start.Note);

        await using ILiveTranscoder running = start.Transcoder!;
        using CancellationTokenSource stopping = new(TimeSpan.FromSeconds(30));
        Stopwatch since = Stopwatch.StartNew();
        Task feeding = FeedAsync(running.Input, bytes.AsMemory(joined), bytesPerSecond, since, stopping.Token);
        Task<TimeSpan> firstPicture = FirstPictureAsync(running.Output, since, stopping.Token);

        await running.Probed.WaitAsync(stopping.Token);

        TimeSpan probed = since.Elapsed;
        TimeSpan pictured = await firstPicture;

        await stopping.CancelAsync();
        await Quietly(feeding);

        Assert.True(probed <= LongestProbe, $"the input was read for {probed.TotalSeconds:F2} s before decoding started");
        Assert.True(
            pictured <= LongestToTheFirstPicture,
            $"the first picture was written {pictured.TotalSeconds:F2} s after the broadcast began to arrive");
        Assert.True(probed < pictured, "the first picture was written before the input was read");
    }

    private static SyntheticBroadcast Of(string broadcast)
        => broadcast switch
        {
            "measured" => SyntheticBroadcast.AsMeasured(),
            "standard definition" => SyntheticBroadcast.Of(SyntheticPicture.StandardDefinition),
            "full hd" => SyntheticBroadcast.Of(SyntheticPicture.FullHd),
            "dual mono" => SyntheticBroadcast.Sounding(SyntheticSound.DualMono),
            _ => throw new ArgumentOutOfRangeException(nameof(broadcast), broadcast, "A broadcast this test writes."),
        };

    private LiveTranscoderFactory Factory()
        => new(
            new LiveTranscodeSettings { Programme = Ffmpeg(), StopGrace = TimeSpan.FromSeconds(2) },
            budget,
            new SoftwareAlone(),
            new MachineSettings(),
            TimeProvider.System);

    /// <summary>
    /// The ffmpeg the synthetic broadcast was written with, named by its path so it is found off the path the
    /// application searches as well.
    /// </summary>
    private static string Ffmpeg()
        => (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, FfmpegProgramme.Default))
            .FirstOrDefault(File.Exists) ?? FfmpegProgramme.Default;

    private static async Task FeedAsync(
        Stream into,
        ReadOnlyMemory<byte> broadcast,
        double bytesPerSecond,
        Stopwatch since,
        CancellationToken cancellationToken)
    {
        int sent = 0;

        while (sent < broadcast.Length && !cancellationToken.IsCancellationRequested)
        {
            int next = Math.Min(PacketLength * PacketsAtATime, broadcast.Length - sent);

            await into.WriteAsync(broadcast.Slice(sent, next), cancellationToken);
            await into.FlushAsync(cancellationToken);
            sent += next;

            TimeSpan due = TimeSpan.FromSeconds(sent / bytesPerSecond) - since.Elapsed;

            if (due > TimeSpan.Zero)
            {
                await Task.Delay(due, cancellationToken);
            }
        }
    }

    private static async Task<TimeSpan> FirstPictureAsync(Stream from, Stopwatch since, CancellationToken cancellationToken)
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);
        byte[] mouthful = new byte[LiveFeed.Mouthful];
        int read;

        while ((read = await from.ReadAsync(mouthful, cancellationToken)) > 0)
        {
            LiveFragmenting made = fragmenter.Read(mouthful.AsSpan(0, read));

            Assert.Null(made.Fault);

            if (made.Fragments.Any(fragment => fragment.Kind is LiveFragmentKind.Media))
            {
                return since.Elapsed;
            }
        }

        throw new InvalidOperationException("the transcoder ended without writing a picture");
    }

    private static async Task Quietly(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception gone) when (gone is OperationCanceledException or IOException or ObjectDisposedException)
        {
            return;
        }
    }

    private sealed class SoftwareAlone : ILiveEncoderSelector
    {
        public Task<LiveEncoderChoice> ChooseAsync(CancellationToken cancellationToken)
            => Task.FromResult(LiveEncoderChoice.Asked(LiveEncoder.Software));
    }
}
