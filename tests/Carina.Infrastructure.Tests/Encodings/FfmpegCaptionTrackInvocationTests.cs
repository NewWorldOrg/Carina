using Carina.Infrastructure.Encodings;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class FfmpegCaptionTrackInvocationTests
{
    [Fact]
    public void BrEd2019PictureAndSoundAreCopiedAndTheCaptionsGoInAsOneJapaneseTextTrackOffByDefault()
    {
        string[] arguments = [.. FfmpegCaptionTrackInvocation.Arguments("/encodes/a.mp4", "/encodes/a.vtt", "/encodes/a.captioned")];

        Assert.Equal(["-i", "/encodes/a.mp4", "-f", "webvtt", "-i", "/encodes/a.vtt"], arguments[5..11]);
        Assert.Equal(["-map", "0:v", "-map", "0:a?", "-map", "1:0"], arguments[11..17]);
        Assert.Equal(["-c", "copy", "-c:s", "mov_text"], arguments[Array.IndexOf(arguments, "-c")..(Array.IndexOf(arguments, "-c") + 4)]);
        Assert.Equal("language=jpn", arguments[Array.IndexOf(arguments, "-metadata:s:s:0") + 1]);
        Assert.Equal("0", arguments[Array.IndexOf(arguments, "-disposition:s:0") + 1]);
        Assert.Equal("0", arguments[Array.IndexOf(arguments, "-map_chapters") + 1]);
        Assert.Equal(["-f", "mp4", "-movflags", "faststart", "/encodes/a.captioned"], arguments[^5..]);
        Assert.DoesNotContain(arguments, argument => argument is "-ss" or "-vf" or "-c:v" or "-c:a");
    }

    [Fact]
    public void WhatAFileCarriesIsAskedForAsJson()
    {
        string[] arguments = [.. FfmpegCaptionTrackInvocation.Carried("/encodes/a.mp4")];

        Assert.Equal(["-of", "json", "-show_entries", FfmpegCaptionTrackInvocation.Entries, "-i", "/encodes/a.mp4"], arguments[3..]);
    }
}
