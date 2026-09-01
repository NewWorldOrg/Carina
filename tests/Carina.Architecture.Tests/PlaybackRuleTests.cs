namespace Carina.Architecture.Tests;

public sealed class PlaybackRuleTests
{
    private const string Delivery = "/Carina.Api/Playback/VideoDelivery.cs";

    private const string WhereItIsMapped = "/Carina.Api/Program.cs";

    private const string WhereTheDocumentSaysItExists = "/Carina.Api/OpenApi/ApiDocumentTransformer.cs";

    [Fact]
    public void TheOnlyPlacesThatSpellTheDeliveryPathAreWhereItIsDeclaredAndWhereTheDocumentDisownsIt()
    {
        Assert.Equal(
            [WhereTheDocumentSaysItExists, Delivery],
            PlaybackRules.FilesSpellingTheDeliveryPath(RepositoryLayout.SourceDirectory));
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
    public void NothingOnTheDeliveryPathTranscodesWhileItPlays()
    {
        Assert.Empty(PlaybackRules.WhatTheDeliveryTranscodes(RepositoryLayout.SourceDirectory));
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
