using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.Infrastructure.Tests.Integrity;

namespace Carina.Infrastructure.Tests.Segments;

public sealed class ReducedCopyReaderTests : IDisposable
{
    private static readonly DateTime Starts = CopyDescription.Starts;

    private readonly TempTree shelf = new();

    public static TheoryData<string> EveryFile
        => [ReducedCopy.Manifest, ReducedCopy.Rows, ReducedCopy.Probe, ReducedCopy.Sum, ReducedCopy.Difference, ReducedCopy.Frames, ReducedCopy.Corners];

    public void Dispose() => shelf.Dispose();

    [Fact(DisplayName = "a copy names the recording it was made from and copies its programme from the rows copied beside it")]
    public void ACopyNamesItsRecordingAndCopiesItsProgramme()
    {
        CopyDescription description = new() { Name = "A programme \U0001F14A", Audio = "DualMono" };
        string directory = ReducedCopies.Write(shelf.Root, "one", description);

        ReducedCopyRead read = ReducedCopyReader.Read(directory);

        ReducedCopy copy = Assert.IsType<ReducedCopy>(read.Copy);
        Assert.Null(read.Refusal);
        Assert.Equal((directory, description.Id), (copy.Directory, copy.Id));
        Assert.Equal(
            new ProgrammeCopy(
                new NetworkId(description.Network),
                new ServiceId(description.Service),
                Starts,
                Starts.AddMinutes(30),
                Starts.AddSeconds(-5),
                "A programme \U0001F14A",
                [new ProgrammeGenre(7, 0), new ProgrammeGenre(7, 1)],
                [ProgrammeMark.HighDefinition],
                null,
                AudioMode.DualMono,
                null),
            copy.Programme);
        Assert.Null(copy.Captions);
        Assert.Equal(TimeSpan.FromSeconds(0.2), copy.StartsAt);
    }

    [Theory(DisplayName = "the copy starts on the recording's own time where the programme's first picture or first sound began, whichever came first, whatever else began before them")]
    [InlineData("1000.900000", "1000.700000", 0.2)]
    [InlineData("1000.600000", "1001.700000", 0.1)]
    [InlineData(null, "1002.250000", 1.75)]
    [InlineData("1000.500000", "1000.500000", 0)]
    public void TheCopyStartsWhereThePictureOrSoundBegan(string? picture, string? sound, double seconds)
    {
        ReducedCopy copy = Assert.IsType<ReducedCopy>(
            ReducedCopyReader.Read(ReducedCopies.Write(shelf.Root, "starts", new CopyDescription { PictureBegins = picture, SoundBegins = sound })).Copy);

        Assert.Equal(TimeSpan.FromSeconds(seconds), copy.StartsAt);
    }

    [Theory(DisplayName = "a copy whose probe does not say where its programme began on the file's clock is not imported")]
    [InlineData(4322, "1000.900000", "1000.700000")]
    [InlineData(null, null, null)]
    [InlineData(null, "1000.400000", "1000.450000")]
    public void ACopyWhoseProbeDoesNotSayWhereItBeganIsNotImported(int? probed, string? picture, string? sound)
    {
        ReducedCopyRead read = ReducedCopyReader.Read(
            ReducedCopies.Write(shelf.Root, "unplaced", new CopyDescription { Probed = probed, PictureBegins = picture, SoundBegins = sound }));

        Assert.Null(read.Copy);
        Assert.Contains(ReducedCopy.Probe, read.Refusal, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a copy that carries captions names the file they are in")]
    public void ACopyThatCarriesCaptionsNamesTheirFile()
    {
        string directory = ReducedCopies.Write(shelf.Root, "captioned", new CopyDescription());
        File.WriteAllBytes(Path.Combine(directory, ReducedCopy.CaptionsFile), [1]);

        ReducedCopy copy = Assert.IsType<ReducedCopy>(ReducedCopyReader.Read(directory).Copy);

        Assert.Equal(Path.Combine(directory, ReducedCopy.CaptionsFile), copy.Captions);
    }

    [Theory(DisplayName = "the programme ends when the guide says, else when its reservation says once that end was announced, and nowhere when neither says or it would end before it starts")]
    [InlineData(30, null, false, 30)]
    [InlineData(null, 45, true, 45)]
    [InlineData(null, 45, false, null)]
    [InlineData(-10, 45, true, 45)]
    [InlineData(null, 0, true, null)]
    [InlineData(null, null, false, null)]
    public void TheProgrammeEndsWhenTheGuideOrTheReservationSays(int? guided, int? reserved, bool announced, int? ends)
    {
        CopyDescription description = new()
        {
            GuideEndsAt = guided is { } byTheGuide ? Starts.AddMinutes(byTheGuide) : null,
            ReservationEndsAt = reserved is { } byTheReservation ? Starts.AddMinutes(byTheReservation) : null,
            ReservationEndAnnounced = announced,
            Reserved = reserved is not null,
        };

        ReducedCopy copy = Assert.IsType<ReducedCopy>(ReducedCopyReader.Read(ReducedCopies.Write(shelf.Root, "ends", description)).Copy);

        Assert.Equal(ends is { } after ? Starts.AddMinutes(after) : null, copy.Programme.ProgrammeEndsAt);
    }

    [Theory(DisplayName = "a copy whose manifest names another shape, or none, is not imported")]
    [InlineData("anime-material-v2")]
    [InlineData("")]
    [InlineData(null)]
    public void ACopyOfAnotherShapeIsNotImported(string? shape)
    {
        ReducedCopyRead read = ReducedCopyReader.Read(ReducedCopies.Write(shelf.Root, "other", new CopyDescription { Shape = shape }));

        Assert.Null(read.Copy);
        Assert.Contains(ReducedCopy.Shape, read.Refusal, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "a copy that lacks any of the files it is read from is not imported, and says which")]
    [MemberData(nameof(EveryFile))]
    public void ACopyThatLacksAFileIsNotImported(string file)
    {
        ReducedCopyRead read = ReducedCopyReader.Read(ReducedCopies.Write(shelf.Root, "short", new CopyDescription(), file));

        Assert.Null(read.Copy);
        Assert.Contains(file, read.Refusal, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a copy with an empty sound or picture is not imported")]
    public void ACopyWithAnEmptyMediumIsNotImported()
    {
        string directory = ReducedCopies.Write(shelf.Root, "empty", new CopyDescription());
        File.WriteAllBytes(Path.Combine(directory, ReducedCopy.Corners), []);

        ReducedCopyRead read = ReducedCopyReader.Read(directory);

        Assert.Null(read.Copy);
        Assert.Contains(ReducedCopy.Corners, read.Refusal, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a copy whose manifest names another recording than its rows is not imported")]
    public void ACopyNamingTwoRecordingsIsNotImported()
    {
        ReducedCopyRead read = ReducedCopyReader.Read(
            ReducedCopies.Write(shelf.Root, "mixed", new CopyDescription { ManifestNames = Guid.NewGuid() }));

        Assert.Null(read.Copy);
        Assert.NotNull(read.Refusal);
    }

    [Theory(DisplayName = "a copy whose descriptions cannot be read is not imported, and says why")]
    [InlineData(ReducedCopy.Manifest, "{ not json")]
    [InlineData(ReducedCopy.Rows, "{ not json")]
    [InlineData(ReducedCopy.Rows, "[]")]
    [InlineData(ReducedCopy.Rows, """{ "recording": null }""")]
    [InlineData(ReducedCopy.Rows, """{ "recording": { "id": "not an id" } }""")]
    public void ACopyWhoseDescriptionsCannotBeReadIsNotImported(string file, string written)
    {
        string directory = ReducedCopies.Write(shelf.Root, "garbled", new CopyDescription());
        File.WriteAllText(Path.Combine(directory, file), written);

        ReducedCopyRead read = ReducedCopyReader.Read(directory);

        Assert.Null(read.Copy);
        Assert.False(string.IsNullOrWhiteSpace(read.Refusal));
    }

    [Theory(DisplayName = "a copy whose rows hold what no recording could is not imported")]
    [InlineData("Loud", 1)]
    [InlineData("Stereo", 70000)]
    public void ACopyWhoseRowsHoldWhatNoRecordingCouldIsNotImported(string audio, int service)
    {
        ReducedCopyRead read = ReducedCopyReader.Read(
            ReducedCopies.Write(shelf.Root, "impossible", new CopyDescription { Audio = audio, Service = service }));

        Assert.Null(read.Copy);
        Assert.False(string.IsNullOrWhiteSpace(read.Refusal));
    }

    [Fact(DisplayName = "descriptions too large to be what the copy says of itself are not read")]
    public void DescriptionsTooLargeAreNotRead()
    {
        string directory = ReducedCopies.Write(shelf.Root, "large", new CopyDescription());
        File.WriteAllText(Path.Combine(directory, ReducedCopy.Rows), new string(' ', (int)ReducedCopyReader.LargestDescription + 1));

        ReducedCopyRead read = ReducedCopyReader.Read(directory);

        Assert.Null(read.Copy);
        Assert.Contains(ReducedCopy.Rows, read.Refusal, StringComparison.Ordinal);
    }
}
