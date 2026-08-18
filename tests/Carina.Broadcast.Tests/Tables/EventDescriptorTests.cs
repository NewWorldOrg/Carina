using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Tables;

public sealed class EventDescriptorTests
{
    private const int SomeService = 1024;

    [Fact]
    public void EveryGenrePairIsReadInTheOrderItWasBroadcast()
    {
        var genres = Read(DescriptorTags.Content, [0x0F, 0xFF, 0xB5, 0xFF]).Genres;

        Assert.Equal([0, 11], genres.Select(genre => genre.Kind));
        Assert.Equal([15, 5], genres.Select(genre => genre.Sort));
        Assert.Equal([15, 15], genres.Select(genre => genre.UserKind));
        Assert.Equal([15, 15], genres.Select(genre => genre.UserSort));
    }

    [Fact]
    public void AGenreListCutBetweenItsHalvesIsNotRead()
    {
        Assert.Empty(Read(DescriptorTags.Content, [0x0F, 0xFF, 0xB5]).Genres);
    }

    [Fact]
    public void AVideoStreamNamesWhatItCarries()
    {
        var video = Assert.Single(Read(DescriptorTags.Component, [0xF1, 0xB3, 0x00, 0x6A, 0x70, 0x6E]).Components);

        Assert.Equal(1, video.StreamContent);
        Assert.Equal(0xB3, video.ComponentType);
        Assert.Equal(0, video.ComponentTag);
        Assert.Equal("jpn", video.Language);
        Assert.Equal(string.Empty, video.Text);
    }

    [Fact]
    public void AVideoStreamWithoutRoomForItsOwnHeaderIsNotRead()
    {
        Assert.Empty(Read(DescriptorTags.Component, [0xF1, 0xB3, 0x00, 0x6A, 0x70]).Components);
    }

    [Fact]
    public void AnAudioStreamNamesHowItSounds()
    {
        var audio = Assert.Single(Read(
            DescriptorTags.AudioComponent,
            [0xF2, 0x03, 0x10, 0x0F, 0xFF, 0x6F, 0x6A, 0x70, 0x6E]).AudioComponents);

        Assert.Equal(2, audio.StreamContent);
        Assert.Equal(0x03, audio.ComponentType);
        Assert.Equal(0x10, audio.ComponentTag);
        Assert.Equal(0x0F, audio.StreamType);
        Assert.True(audio.IsMainComponent);
        Assert.Equal(2, audio.QualityIndicator);
        Assert.Equal(7, audio.SamplingRate);
        Assert.Equal("jpn", audio.Language);
        Assert.Equal(string.Empty, audio.SecondLanguage);
    }

    [Fact]
    public void AnAudioStreamInTwoLanguagesNamesTheSecondOneAndStillReadsItsText()
    {
        var audio = Assert.Single(Read(
            DescriptorTags.AudioComponent,
            [
                0xF2, 0x03, 0x10, 0x0F, 0xFF, 0xEF,
                0x6A, 0x70, 0x6E,
                0x65, 0x6E, 0x67,
                0x1B, 0x28, 0x4A, 0x41, 0x42,
            ]).AudioComponents);

        Assert.Equal("jpn", audio.Language);
        Assert.Equal("eng", audio.SecondLanguage);
        Assert.Equal("AB", audio.Text);
    }

    [Fact]
    public void AnAudioStreamInOneLanguageReadsItsTextFromRightAfterThatLanguage()
    {
        var audio = Assert.Single(Read(
            DescriptorTags.AudioComponent,
            [
                0xF2, 0x03, 0x10, 0x0F, 0xFF, 0x6F,
                0x6A, 0x70, 0x6E,
                0x1B, 0x28, 0x4A, 0x41, 0x42,
            ]).AudioComponents);

        Assert.Equal(string.Empty, audio.SecondLanguage);
        Assert.Equal("AB", audio.Text);
    }

    [Fact]
    public void AnAudioStreamPromisingASecondLanguageItDoesNotCarryIsNotRead()
    {
        Assert.Empty(Read(
            DescriptorTags.AudioComponent,
            [0xF2, 0x03, 0x10, 0x0F, 0xFF, 0xEF, 0x6A, 0x70, 0x6E, 0x65]).AudioComponents);
    }

    [Fact]
    public void EventsBroadcastTogetherNameEachOther()
    {
        var grouping = Assert.Single(Read(
            DescriptorTags.EventGroup,
            [0x12, 0x04, 0x18, 0xB8, 0xC4, 0x04, 0x19, 0xB8, 0xC4]).Groupings);

        Assert.Equal(EventGroupKind.Shared, grouping.Kind);

        Assert.Equal(
            [(1048, 47300), (1049, 47300)],
            grouping.Events.Select(carried => (carried.ServiceId, carried.EventId)));
    }

    [Fact]
    public void EventsCarriedOverFromAnotherNetworkNameBothSidesOfTheHandover()
    {
        var grouping = Assert.Single(Read(
            DescriptorTags.EventGroup,
            [
                0x41,
                0x04, 0x18, 0xB8, 0xC4,
                0x7F, 0xE3, 0x7F, 0xE4, 0x04, 0x19, 0xB8, 0xC5,
            ]).Groupings);

        Assert.Equal(EventGroupKind.RelayedFromAnotherNetwork, grouping.Kind);

        var here = Assert.Single(grouping.Events);

        Assert.Equal(1048, here.ServiceId);
        Assert.Equal(47300, here.EventId);

        var there = Assert.Single(grouping.Elsewhere);

        Assert.Equal(32739, there.NetworkId);
        Assert.Equal(32740, there.TransportStreamId);
        Assert.Equal(1049, there.ServiceId);
        Assert.Equal(47301, there.EventId);
    }

    [Fact]
    public void AHandoverWhoseFarSideIsCutShortIsNotRead()
    {
        Assert.Empty(Read(
            DescriptorTags.EventGroup,
            [0x41, 0x04, 0x18, 0xB8, 0xC4, 0x7F, 0xE3, 0x7F, 0xE4]).Groupings);
    }

    [Fact]
    public void AGroupInThisNetworkCarriesNothingFromAnywhereElse()
    {
        var grouping = Assert.Single(Read(
            DescriptorTags.EventGroup,
            [0x12, 0x04, 0x18, 0xB8, 0xC4, 0x04, 0x19, 0xB8, 0xC4]).Groupings);

        Assert.Empty(grouping.Elsewhere);
    }

    [Fact]
    public void AGroupPromisingMoreEventsThanItCarriesIsNotRead()
    {
        Assert.Empty(Read(DescriptorTags.EventGroup, [0x12, 0x04, 0x18, 0xB8, 0xC4]).Groupings);
    }

    [Fact]
    public void AGroupOfAKindThisLibraryDoesNotKnowStillNamesItsEvents()
    {
        var grouping = Assert.Single(Read(DescriptorTags.EventGroup, [0x61, 0x04, 0x18, 0xB8, 0xC4]).Groupings);

        Assert.Equal(EventGroupKind.Undefined, grouping.Kind);
        Assert.Single(grouping.Events);
    }

    private static DescribedEvent Read(byte tag, byte[] payload)
    {
        byte[] descriptor = [tag, (byte)payload.Length, .. payload];

        var table = Assert.IsType<TableRead<EventInformationTable>.Parsed>(
            EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = EventInformationTable.PresentFollowingActualTableId,
                TableIdExtension = SomeService,
                Body = [.. Header(), .. Event(descriptor.Length), .. descriptor],
            }))).Table;

        return Assert.Single(table.Events);
    }

    private static byte[] Header() => [0x7F, 0xE3, 0x7F, 0xE3, 0x00, 0x4E];

    private static byte[] Event(int descriptorsLength)
        =>
        [
            0x00, 0x01,
            0xEF, 0x55, 0x22, 0x57, 0x00,
            0x00, 0x03, 0x00,
            (byte)((descriptorsLength >> 8) & 0x0F), (byte)(descriptorsLength & 0xFF),
        ];
}
