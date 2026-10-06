using System.Runtime.Versioning;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Machines;
using Carina.Domain.Streaming;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Machines;
using Carina.Infrastructure.Streaming;
using Carina.Infrastructure.Thumbnails;

namespace Carina.Infrastructure.Tests.Machines;

[SupportedOSPlatform("linux")]
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ChildProcessEnvironmentTests : IDisposable
{
    private const string Secret = "CARINA_DB_CONNECTION";

    private readonly StandIns standIns = new();

    private readonly string? held = Environment.GetEnvironmentVariable(Secret);

    public ChildProcessEnvironmentTests() => Environment.SetEnvironmentVariable(Secret, "Host=db;Password=hunter2");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Secret, held);
        standIns.Dispose();
    }

    [Fact(DisplayName = "the programme that draws a thumbnail is given nothing of this process's environment")]
    public async Task TheProgrammeThatDrawsAThumbnailIsGivenNothingOfThisProcesssEnvironment()
    {
        string seen = standIns.Named("environment");
        string source = standIns.Named("recording.m2ts");
        await File.WriteAllTextAsync(source, "not really a transport stream");
        FfmpegThumbnailRenderer renderer = new(
            new ThumbnailSettings { Programme = standIns.Script($"env > \"{seen}\"; exit 1") },
            TimeProvider.System);

        await renderer.RenderAsync(
            new ThumbnailRequest(source, standIns.Named("one.jpg"), new ServiceId(1032), TimeSpan.FromSeconds(1)),
            CancellationToken.None);

        AssertGivenOnlyTheSearchPath(seen);
    }

    [Fact(DisplayName = "the programme that reads a stream's attributes is given nothing of this process's environment")]
    public async Task TheProgrammeThatReadsAStreamsAttributesIsGivenNothingOfThisProcesssEnvironment()
    {
        string seen = standIns.Named("environment");
        FfprobeStreamAttributeReader reader = new(
            new StreamAttributeSettings { Programme = standIns.Script($"env > \"{seen}\"; exit 1") },
            TimeProvider.System);

        await reader.ReadAsync(new StreamSource("/srv/recordings/k-1.ts"), CancellationToken.None);

        AssertGivenOnlyTheSearchPath(seen);
    }

    [Fact(DisplayName = "the transcoder of a live picture is given nothing of this process's environment")]
    public async Task TheTranscoderOfALivePictureIsGivenNothingOfThisProcesssEnvironment()
    {
        string seen = standIns.Named("environment");
        LiveTranscoderFactory factory = new(
            new LiveTranscodeSettings { Programme = standIns.Script($"env > \"{seen}\"; cat > /dev/null") },
            new TranscodeBudget(new TranscodeBudgetSettings { AtOnce = 1 }),
            new Software(),
            new MachineSettings(),
            TimeProvider.System);

        LiveTranscoderStart start = await factory.StartAsync(
            new ServiceId(1040),
            LiveProfile.Hd30,
            SoundPlacement.WholeStream(0),
            new StreamAttributes(new VideoSize(1440, 1080), ScanType.Interlaced, FrameRate.BroadcastFrames, AudioMode.Stereo),
            CaptionOutlet.None,
            CancellationToken.None);

        Assert.True(start.Running, start.Note);

        await using ILiveTranscoder running = start.Transcoder!;
        await running.Input.DisposeAsync();
        await running.Completion;

        AssertGivenOnlyTheSearchPath(seen);
    }

    private static void AssertGivenOnlyTheSearchPath(string seen)
    {
        string[] environment = File.ReadAllLines(seen);

        Assert.DoesNotContain(environment, line => line.StartsWith($"{Secret}=", StringComparison.Ordinal));
        Assert.Contains($"PATH={AnotherProgramme.SearchedIn}", environment);
    }

    private sealed class Software : ILiveEncoderSelector
    {
        public Task<LiveEncoderChoice> ChooseAsync(CancellationToken cancellationToken)
            => Task.FromResult(LiveEncoderChoice.Asked(LiveEncoder.Software));
    }
}
