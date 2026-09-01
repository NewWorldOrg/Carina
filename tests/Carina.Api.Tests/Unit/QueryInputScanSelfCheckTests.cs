using Carina.Api.Tests.FeatureTest;

namespace Carina.Api.Tests.Unit;

public sealed class QueryInputScanSelfCheckTests
{
    [Fact]
    public void DetectsAQueryNameSpeltWhereItIsRead()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Playback/ScrubDelivery.cs",
            """
            public const string Path = "/api/videos/{id}/scrub";
            context.Request.Query["at"];
            """);

        Assert.Equal(
            ["/api/videos/{id}/scrub at"],
            Read(tree));
    }

    [Fact]
    public void DetectsAQueryNameHeldInAConstantBesideTheReading()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Playback/PlayDelivery.cs",
            """
            public const string Path = "/api/videos/{id}/play";
            public const string Quality = "profile";
            context.Request.Query[Quality];
            """);

        Assert.Equal(
            ["/api/videos/{id}/play profile"],
            Read(tree));
    }

    [Fact]
    public void DetectsAQueryAskedForByTheOtherOrdinaryWayOfAskingForOne()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Playback/ScrubDelivery.cs",
            """
            public const string Path = "/api/videos/{id}/scrub";
            context.Request.Query.TryGetValue("at", out StringValues asked);
            """);

        Assert.Equal(["/api/videos/{id}/scrub at"], Read(tree));
    }

    [Fact]
    public void DetectsAControllerReadingAQueryByTheRouteItDeclares()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Controllers/Recordings/ListRecordingsAction.cs",
            """
            [Route("api/recordings")]
            Request.Query["page"];
            """);

        Assert.Equal(["/api/recordings page"], Read(tree));
    }

    [Fact]
    public void ReportsANameSpeltSomewhereItCannotFollow()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Ipc/DriverApi.cs",
            """
            public const string Path = "/sessions";
            context.Request.Query[DriverEndpoints.OutputRootQuery];
            """);

        Assert.Empty(Read(tree));
        Assert.Equal(
            ["/Ipc/DriverApi.cs cannot resolve DriverEndpoints.OutputRootQuery"],
            QueryInputScan.WhatTheScanCouldNotPlace(tree.Root));
    }

    [Fact]
    public void ReportsAReadingItCannotPutOnASurface()
    {
        using var tree = new SourceTree();
        tree.Write("Common/Middleware.cs", """context.Request.Query["cursor"];""");

        Assert.Empty(Read(tree));
        Assert.Equal(
            ["/Common/Middleware.cs no route for \"cursor\""],
            QueryInputScan.WhatTheScanCouldNotPlace(tree.Root));
    }

    [Fact]
    public void ReportsAQueryMovedIntoAHelperOfItsOwnRatherThanLettingItPass()
    {
        using var tree = new SourceTree();
        tree.Write("Playback/ThumbnailWidthAsked.cs", """request?.Query["width"];""");

        Assert.Empty(Read(tree));
        Assert.Equal(
            ["/Playback/ThumbnailWidthAsked.cs no route for \"width\""],
            QueryInputScan.WhatTheScanCouldNotPlace(tree.Root));
    }

    [Fact]
    public void CannotSeeAQueryReadWithoutNamingWhatItReads()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Controllers/Epg/SearchProgrammesAction.cs",
            """
            [Route("api/programs/search")]
            ProgrammeSearchQuery.Read(Request.QueryString.Value);
            bool anything = Request.Query.Any(asked => asked.Key.Length > 0);
            """);

        Assert.Empty(Read(tree));
        Assert.Empty(QueryInputScan.WhatTheScanCouldNotPlace(tree.Root));
    }

    [Fact]
    public void ReportsANamePutTogetherOutOfPiecesRatherThanReadingIt()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Playback/ScrubDelivery.cs",
            """
            public const string Path = "/api/videos/{id}/scrub";
            context.Request.Query["a" + "t"];
            """);

        Assert.Empty(Read(tree));
        Assert.Equal(
            ["/Playback/ScrubDelivery.cs cannot resolve \"a\" + \"t\""],
            QueryInputScan.WhatTheScanCouldNotPlace(tree.Root));
    }

    [Fact]
    public void ReadsNothingAboutHeadersOrCookiesOrWhatArrivesInABody()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Playback/PlayDelivery.cs",
            """
            public const string Path = "/api/videos/{id}/play";
            context.Request.Headers.Range;
            context.Request.Headers["Authorization"];
            context.Request.Cookies["carina"];
            context.Request.Form["profile"];
            """);

        Assert.Empty(Read(tree));
        Assert.Empty(QueryInputScan.WhatTheScanCouldNotPlace(tree.Root));
    }

    [Fact]
    public void ReadsNothingOutOfAnEmptyTree()
    {
        using var tree = new SourceTree();

        Assert.Empty(Read(tree));
        Assert.Empty(QueryInputScan.WhatTheScanCouldNotPlace(tree.Root));
    }

    private static string[] Read(SourceTree tree)
        => [.. QueryInputScan.WhatEachSurfaceReads(tree.Root).Select(read => read.ToString())];

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-query-inputs-");

        public string Root => directory.FullName;

        public void Write(string path, string source)
        {
            string full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, source);
        }

        public void Dispose() => directory.Delete(recursive: true);
    }
}
