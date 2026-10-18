using Carina.Infrastructure.Encodings;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class FfmpegChapterRewriteInvocationTests
{
    [Fact(DisplayName = "BR-CD-010: the copy takes the chapters from the file after the artefact and copies the picture, the sound and the subtitles as they are, leaving the artefact's own chapter track behind")]
    public void TheCopyTakesTheChaptersFromTheFileAfterTheArtefact()
    {
        IReadOnlyList<string> arguments = FfmpegChapterRewriteInvocation.Arguments("/shelf/a.mp4", "/shelf/a.chapters", "/shelf/a.chaptered");

        Assert.Equal(
            [
                "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
                "-i", "/shelf/a.mp4",
                "-f", "ffmetadata", "-i", "/shelf/a.chapters",
                "-map", "0:v", "-map", "0:a?", "-map", "0:s?",
                "-map_chapters", "1",
                "-map_metadata", "0",
                "-c", "copy",
                "-f", "mp4", "-movflags", "faststart",
                "/shelf/a.chaptered",
            ],
            arguments);
    }

    [Fact(DisplayName = "BR-CD-010: with no chapters to write the copy reads no chapters file and carries none")]
    public void WithNoChaptersTheCopyCarriesNone()
    {
        IReadOnlyList<string> arguments = FfmpegChapterRewriteInvocation.Arguments("/shelf/a.mp4", null, "/shelf/a.chaptered");

        Assert.DoesNotContain("ffmetadata", arguments);
        Assert.Equal(1, arguments.Count(argument => argument == "-i"));
        Assert.Equal(["-map_chapters", "-1"], arguments.SkipWhile(argument => argument != "-map_chapters").Take(2));
    }

    [Fact]
    public void TheChaptersAFileCarriesAreAskedForAsJson()
        => Assert.Equal(
            ["-hide_banner", "-loglevel", "error", "-of", "json", "-show_chapters", "-i", "/shelf/a.mp4"],
            FfmpegChapterRewriteInvocation.Chapters("/shelf/a.mp4"));
}
