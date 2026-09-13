using Carina.Broadcast.Descriptors;
using Carina.Domain.Base;
using Carina.Infrastructure.Collection;

namespace Carina.Infrastructure.Tests.Collection;

public sealed class AnnouncedVideoTests
{
    [Theory]
    [InlineData(0x01, VideoMode.Interlaced480)]
    [InlineData(0x04, VideoMode.Interlaced480)]
    [InlineData(0x83, VideoMode.Progressive4320)]
    [InlineData(0x91, VideoMode.Progressive2160)]
    [InlineData(0xA3, VideoMode.Progressive480)]
    [InlineData(0xB3, VideoMode.Interlaced1080)]
    [InlineData(0xC1, VideoMode.Progressive720)]
    [InlineData(0xD3, VideoMode.Progressive240)]
    [InlineData(0xE4, VideoMode.Progressive1080)]
    [InlineData(0xF1, VideoMode.Progressive180)]
    public void TheAnnouncedComponentTypeNamesHowManyLinesThePictureHas(int componentType, VideoMode expected)
    {
        Assert.Equal(expected, AnnouncedVideo.ModeOf([Component(componentType)]));
    }

    [Theory]
    [InlineData(0xB1, AspectRatio.FourByThree)]
    [InlineData(0xB2, AspectRatio.SixteenByNineWithPanVector)]
    [InlineData(0xB3, AspectRatio.SixteenByNine)]
    [InlineData(0xB4, AspectRatio.WiderThanSixteenByNine)]
    [InlineData(0x01, AspectRatio.FourByThree)]
    [InlineData(0x83, AspectRatio.SixteenByNine)]
    public void TheSameComponentTypeAlsoNamesTheShapeOfThePicture(int componentType, AspectRatio expected)
    {
        Assert.Equal(expected, AnnouncedVideo.AspectOf([Component(componentType)]));
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x05)]
    [InlineData(0x80)]
    [InlineData(0x95)]
    [InlineData(0xB0)]
    public void AComponentTypeThatNamesNoPictureLeavesBothUnanswered(int componentType)
    {
        Assert.Equal(VideoMode.Undetermined, AnnouncedVideo.ModeOf([Component(componentType)]));
        Assert.Equal(AspectRatio.Undetermined, AnnouncedVideo.AspectOf([Component(componentType)]));
    }

    [Fact]
    public void ABroadcastThatAnnouncedNoPictureAtAllLeavesItUnanswered()
    {
        Assert.Equal(VideoMode.Undetermined, AnnouncedVideo.ModeOf([]));
        Assert.Equal(AspectRatio.Undetermined, AnnouncedVideo.AspectOf([]));
    }

    [Fact]
    public void APictureCarriedTheNewerWayIsReadFromTheSameTable()
    {
        Assert.Equal(
            VideoMode.Progressive720,
            AnnouncedVideo.ModeOf([Component(0xC3, streamContent: 5)]));
    }

    [Fact]
    public void AComponentThatIsNotAPictureIsPassedOverRatherThanReadAsOne()
    {
        Assert.Equal(
            VideoMode.Interlaced1080,
            AnnouncedVideo.ModeOf([Component(0x03, streamContent: 2), Component(0xB3)]));
    }

    [Fact]
    public void TheMainPictureIsTheOneReadEvenWhenAnotherWasAnnouncedFirst()
    {
        Assert.Equal(
            VideoMode.Interlaced1080,
            AnnouncedVideo.ModeOf([Component(0xC3, componentTag: 0x01), Component(0xB3, componentTag: 0x00)]));
    }

    [Fact]
    public void TheMainPictureAlsoNamesTheShapeEvenWhenAnotherWasAnnouncedFirst()
    {
        Assert.Equal(
            AspectRatio.FourByThree,
            AnnouncedVideo.AspectOf([Component(0xC3, componentTag: 0x01), Component(0xB1, componentTag: 0x00)]));
    }

    [Fact]
    public void WhenNoPictureCarriesTheMainTagTheFirstAnnouncedIsRead()
    {
        Assert.Equal(
            VideoMode.Progressive720,
            AnnouncedVideo.ModeOf([Component(0xC3, componentTag: 0x01), Component(0xB3, componentTag: 0x02)]));
    }

    [Fact]
    public void NothingIsReadFromAListThatWasNeverHandedOver()
    {
        Assert.Throws<ArgumentNullException>(() => AnnouncedVideo.ModeOf(null!));
        Assert.Throws<ArgumentNullException>(() => AnnouncedVideo.AspectOf(null!));
    }

    private static ComponentDescription Component(int componentType, int streamContent = 1, int componentTag = 0x00)
        => new(streamContent, componentType, componentTag, "jpn", string.Empty);
}
