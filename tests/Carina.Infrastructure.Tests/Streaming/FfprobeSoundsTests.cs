using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class FfprobeSoundsTests
{
    private static readonly ServiceId Service = new(1040);

    private const string TwoSounds = """
        {
            "programs": [
                {
                    "program_id": 1032,
                    "streams": [
                        { "codec_type": "video" },
                        { "codec_type": "audio" }
                    ]
                },
                {
                    "program_id": 1040,
                    "streams": [
                        { "codec_type": "video" },
                        { "codec_type": "audio" },
                        { "codec_type": "audio" },
                        { "codec_type": "subtitle" }
                    ]
                }
            ]
        }
        """;

    private const string OneSound = """
        {
            "programs": [
                {
                    "program_id": 1040,
                    "streams": [
                        { "codec_type": "video" },
                        { "codec_type": "audio" }
                    ]
                }
            ]
        }
        """;

    [Fact]
    public void TheSoundsCountedAreTheOnesInTheServicesOwnProgramme()
    {
        CarriedSounds carried = FfprobeSounds.Read(TwoSounds, Service);

        Assert.True(carried.Known);
        Assert.Equal([SoundTrack.Main, SoundTrack.Secondary], carried.Tracks);
    }

    [Fact]
    public void AServiceCarryingOneSoundOffersOnlyTheMainOne()
    {
        CarriedSounds carried = FfprobeSounds.Read(OneSound, Service);

        Assert.True(carried.Known);
        Assert.Equal([SoundTrack.Main], carried.Tracks);
    }

    [Fact]
    public void AProgrammeOfAnotherServiceInTheSameMultiplexIsNotCounted()
    {
        Assert.Equal([SoundTrack.Main], FfprobeSounds.Read(TwoSounds, new ServiceId(1032)).Tracks);
    }

    [Fact]
    public void AStreamCarryingNoProgrammeOfThisServiceIsNotAnAnswer()
    {
        CarriedSounds carried = FfprobeSounds.Read(OneSound, new ServiceId(1041));

        Assert.False(carried.Known);
        Assert.NotEmpty(carried.Note);
    }

    [Fact]
    public void AProgrammeNamedWithoutStreamsCarriesNoSound()
    {
        CarriedSounds carried = FfprobeSounds.Read("""{ "programs": [ { "program_id": 1040 } ] }""", Service);

        Assert.True(carried.Known);
        Assert.Empty(carried.Tracks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{ }")]
    [InlineData("""{ "programs": 4 }""")]
    [InlineData("[]")]
    public void AnAnswerThisReaderCannotMakeSenseOfIsNotAnAnswer(string said)
    {
        CarriedSounds carried = FfprobeSounds.Read(said, Service);

        Assert.False(carried.Known);
        Assert.NotEmpty(carried.Note);
    }

    [Fact]
    public void AnEmptyListOfProgrammesIsNotAnAnswerEither()
    {
        Assert.False(FfprobeSounds.Read("""{ "programs": [] }""", Service).Known);
    }
}
