using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class RecordedMaterialTests : IDisposable
{
    private static readonly string[] TheStreamsOwnWords =
        ["Video", "Audio", "Resolution", "Scan", "FrameRate", "Component", "Width", "Height", "Aspect", "Codec", "Interlace"];

    private readonly DirectoryInfo mounted = Directory.CreateTempSubdirectory("carina-recorded");

    public void Dispose() => mounted.Delete(recursive: true);

    [Fact]
    public void ARecordingRowNamesTheFilePlacedUnderTheMountedRootAndHasEnded()
    {
        string source = Path.Combine(mounted.FullName, "source.bin");
        File.WriteAllBytes(source, new byte[1_234]);
        RecordingId id = RecordingId.New();

        Recording recording = new RecordedMaterial(mounted, new OutputRoot("bulk")).Ended(id, source);

        Assert.Equal(id, recording.Id);
        Assert.Equal(id.Wire + RecordedMaterial.TransportStream, recording.FileName.Value);
        Assert.True(File.Exists(Path.Combine(mounted.FullName, recording.FileName.Value)));
        Assert.Equal(RecordingOutcome.Complete, recording.Outcome);
        Assert.Equal(1_234L, recording.FileSizeObserved);
        Assert.False(recording.IsInFlight);
        Assert.Equal(RecordedMaterial.SomeServiceId, recording.ServiceId.Value);
    }

    [Fact]
    public void APairIsTwoRowsOfTheSameProgrammeOneForTheTransportStreamAndOneForTheEncodedFile()
    {
        string transportStream = Path.Combine(mounted.FullName, "a.ts");
        string encoded = Path.Combine(mounted.FullName, "a.mp4");
        File.WriteAllBytes(transportStream, new byte[10]);
        File.WriteAllBytes(encoded, new byte[20]);

        RecordedPair pair = new RecordedMaterial(mounted, new OutputRoot("bulk"))
            .Pair(RecordingId.New(), RecordingId.New(), transportStream, encoded);

        Assert.NotEqual(pair.Original.Id, pair.Encoded.Id);
        Assert.Equal(pair.Original.EventId, pair.Encoded.EventId);
        Assert.Equal(pair.Original.ProgrammeStartsAt, pair.Encoded.ProgrammeStartsAt);
        Assert.EndsWith(RecordedMaterial.TransportStream, pair.Original.FileName.Value, StringComparison.Ordinal);
        Assert.EndsWith(RecordedMaterial.Encoded, pair.Encoded.FileName.Value, StringComparison.Ordinal);
        Assert.Equal(20L, pair.Encoded.FileSizeObserved);
    }

    [Fact]
    public void ARecordingRowSaysNothingAboutThePictureOrTheSoundSoBothAreReadFromTheStream()
    {
        string[] said =
        [
            .. typeof(Recording).GetProperties().Select(property => property.Name),
            .. typeof(ProgrammeSnapshot).GetProperties().Select(property => property.Name),
        ];

        Assert.DoesNotContain(
            said,
            name => TheStreamsOwnWords.Any(word => name.Contains(word, StringComparison.Ordinal)));
    }
}
