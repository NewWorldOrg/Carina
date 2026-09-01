namespace Carina.Architecture.Tests;

public sealed class PlaybackRuleTests
{
    private const string Delivery = "/Carina.Api/Playback/VideoDelivery.cs";

    private const string Scrub = "/Carina.Api/Playback/ScrubDelivery.cs";

    private const string Play = "/Carina.Api/Playback/PlayDelivery.cs";

    private const string Picture = "/Carina.Api/Playback/ThumbnailDelivery.cs";

    private const string WhereItIsMapped = "/Carina.Api/Program.cs";

    private const string WhereTheDocumentSaysItExists = "/Carina.Api/OpenApi/ApiDocumentTransformer.cs";

    [Fact]
    public void TheOnlyPlacesThatSpellTheDeliveryPathAreWhereItIsDeclaredAndWhereTheDocumentDisownsIt()
    {
        Assert.Equal(
            [WhereTheDocumentSaysItExists, Play, Scrub, Picture, Delivery],
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
    public void TheOnlyFilesThatKnowTheDeliveryExistsAreItsOwnAndTheOneThatMapsIt()
    {
        Assert.Equal(
            [Delivery, WhereItIsMapped],
            PlaybackRules.FilesNamingTheDelivery(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheDeliveryIsMappedOutOfTheDocumentAWebClientIsGeneratedFrom()
    {
        string mapping = Mapping();

        Assert.Contains("VideoDelivery.Path", mapping, StringComparison.Ordinal);
        Assert.Contains("ExcludeFromDescription()", mapping, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDeliveryAnswersBothTheAskingAndTheAskingForHeadersAlone()
    {
        Assert.Contains("VideoDelivery.Methods", Mapping(), StringComparison.Ordinal);
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

    private static string Mapping()
    {
        string program = File.ReadAllText(
            Path.Combine(RepositoryLayout.SourceDirectory, WhereItIsMapped.TrimStart('/')));

        int at = program.IndexOf("app.MapMethods(VideoDelivery.Path", StringComparison.Ordinal);

        Assert.True(at >= 0, "nothing in the entry point maps the delivery");

        int ends = program.IndexOf(';', at);

        return program[at..ends];
    }
}
