using Carina.Domain.Captions;
using Carina.Domain.Channels;
using Carina.Domain.Machines;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class FfmpegCaptionTranscriberTests
{
    [Fact]
    public async Task BrPd016AFileWhosePictureSizeCannotBeReadIsStillDrawnSoThatOneWithNoCaptionsCanSaySo()
    {
        FfmpegCaptionTranscriber transcriber = new(
            new MachineSettings { Programme = "/nowhere/ffmpeg-that-must-not-run" },
            new CaptionSettings(),
            new UnreadAttributes(),
            TimeProvider.System);

        CaptionTranscription taken = await transcriber.TranscribeAsync("/srv/recordings/k-1.ts", new ServiceId(1040), CancellationToken.None);

        Assert.Equal(CaptionFault.ProgrammeMissing, taken.Fault);
    }

    private sealed class UnreadAttributes : IStreamAttributeReader
    {
        public Task<StreamAttributeReading> ReadAsync(StreamSource source, CancellationToken cancellationToken)
            => Task.FromResult(StreamAttributeReading.Unanswered(StreamProbeFault.TimedOut, "it took too long"));

        public Task<CarriedSounds> SoundsAsync(StreamSource source, ServiceId service, CancellationToken cancellationToken)
            => throw new InvalidOperationException("no sound is asked for when captions are taken");
    }
}
