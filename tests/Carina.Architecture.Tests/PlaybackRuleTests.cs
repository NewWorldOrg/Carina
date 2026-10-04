namespace Carina.Architecture.Tests;

public sealed class PlaybackRuleTests
{
    private const string Delivery = "/Carina.Api/Playback/VideoDelivery.cs";

    private const string Scrub = "/Carina.Api/Playback/ScrubDelivery.cs";

    private const string Play = "/Carina.Api/Playback/PlayDelivery.cs";

    private const string Picture = "/Carina.Api/Playback/ThumbnailDelivery.cs";

    private const string Captions = "/Carina.Api/Playback/CaptionDelivery.cs";

    private const string LiveHandedOver = "/Carina.Api/Live/LiveStreamDelivery.cs";

    private const string WhereItIsMapped = "/Carina.Api/Program.cs";

    private const string WhereTheDocumentSaysItExists = "/Carina.Api/OpenApi/ApiDocumentTransformer.cs";

    [Fact]
    public void TheOnlyPlacesThatSpellTheDeliveryPathAreWhereItIsDeclaredAndWhereTheDocumentDisownsIt()
    {
        Assert.Equal(
            [WhereTheDocumentSaysItExists, Captions, Play, Scrub, Picture, Delivery],
            PlaybackRules.FilesSpellingTheDeliveryPath(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheOtherSurfaceUnderTheSamePrefixIsAPictureAndNotASecondWayToTheBytes()
    {
        string scrub = File.ReadAllText(Path.Combine(RepositoryLayout.SourceDirectory, Scrub.TrimStart('/')));

        Assert.Contains("\"/api/videos/{id}/scrub\"", scrub, StringComparison.Ordinal);
        Assert.Contains("image/jpeg", scrub, StringComparison.Ordinal);
        Assert.DoesNotContain(PlaybackRules.DeliveryEndpoint, scrub, StringComparison.Ordinal);
        Assert.DoesNotContain("Accept-Ranges", scrub, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCaptionsUnderTheSamePrefixAreJsonAndNotASecondWayToTheBytes()
    {
        string captions = File.ReadAllText(Path.Combine(RepositoryLayout.SourceDirectory, Captions.TrimStart('/')));

        Assert.Contains("\"/api/videos/{id}/captions\"", captions, StringComparison.Ordinal);
        Assert.DoesNotContain(PlaybackRules.DeliveryEndpoint, captions, StringComparison.Ordinal);
        Assert.DoesNotContain("Accept-Ranges", captions, StringComparison.Ordinal);
        Assert.Empty(PlaybackRules.WhatTranscodesIn(RepositoryLayout.SourceDirectory, Captions));
    }

    [Fact]
    public void TheOnlyFilesThatKnowTheDeliveryExistsAreItsOwnAndTheOneThatMapsIt()
    {
        Assert.Equal(
            [Delivery, WhereItIsMapped],
            PlaybackRules.FilesNamingTheDelivery(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheDeliveryIsMappedOutOfTheDocumentAWebClientIsGeneratedFrom()
    {
        Assert.Contains("ExcludeFromDescription()", Mapping("VideoDelivery.Path"), StringComparison.Ordinal);
        Assert.Contains(
            "ExcludeFromDescription()",
            Mapping("VideoDelivery.WithTheTicketInThePath"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheDeliveryAnswersBothTheAskingAndTheAskingForHeadersAlone()
    {
        Assert.Contains("VideoDelivery.Methods", Mapping("VideoDelivery.Path"), StringComparison.Ordinal);
        Assert.Contains(
            "VideoDelivery.Methods",
            Mapping("VideoDelivery.WithTheTicketInThePath"),
            StringComparison.Ordinal);
        Assert.Contains(
            "public static readonly string[] Methods = [HttpMethods.Get, HttpMethods.Head];",
            File.ReadAllText(Path.Combine(RepositoryLayout.SourceDirectory, Delivery.TrimStart('/'))),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheOnlySurfaceUnderThisPrefixThatTranscodesWhileItPlaysIsTheOneABrowserPlaysThrough()
    {
        Assert.Equal(
            [Play],
            PlaybackRules.WhatTheDeliveryTranscodes(RepositoryLayout.SourceDirectory));

        Assert.Empty(PlaybackRules.WhatTranscodesIn(RepositoryLayout.SourceDirectory, Delivery));
    }

    [Fact]
    public void TheBrowserSurfaceSaysHowTheRecordingEndedAndWhatSeekingCosts()
    {
        string play = File.ReadAllText(Path.Combine(RepositoryLayout.SourceDirectory, Play.TrimStart('/')));

        Assert.Contains("PlaybackHeaders.Say(context.Response, plan)", play, StringComparison.Ordinal);
        Assert.Contains("PlaybackHeaders.Say(context.Response, viewing.Standing)", play, StringComparison.Ordinal);
        Assert.Contains("AcceptRanges = NoSeeking", play, StringComparison.Ordinal);
        Assert.DoesNotContain(PlaybackRules.DeliveryEndpoint, play, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLiveChannelAnExternalPlayerOpensIsHandedOverWithoutATranscoderToo()
    {
        string handed = File.ReadAllText(
            Path.Combine(RepositoryLayout.SourceDirectory, LiveHandedOver.TrimStart('/')));

        Assert.Contains("video/mp2t", handed, StringComparison.Ordinal);
        Assert.Contains("AdmitForAsLongAsTheGrantLastsAsync", handed, StringComparison.Ordinal);
        Assert.Empty(PlaybackRules.WhatTranscodesIn(RepositoryLayout.SourceDirectory, LiveHandedOver));
        Assert.DoesNotContain(PlaybackRules.DeliveryEndpoint, handed, StringComparison.Ordinal);
    }

    private static string Mapping(string path)
    {
        string program = File.ReadAllText(
            Path.Combine(RepositoryLayout.SourceDirectory, WhereItIsMapped.TrimStart('/')));

        int at = program.IndexOf($"app.MapMethods({path},", StringComparison.Ordinal);

        Assert.True(at >= 0, "nothing in the entry point maps the delivery");

        int ends = program.IndexOf(';', at);

        return program[at..ends];
    }
}
