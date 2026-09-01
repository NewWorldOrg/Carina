using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class FfprobeInvocationTests
{
    [Fact]
    public void TheAnswerIsAskedForByKeyRatherThanByPlace()
    {
        List<string> arguments = [.. FfprobeInvocation.Arguments(new StreamSource("/srv/recordings/k-1.ts"))];

        Assert.Equal("default=nw=1", arguments[arguments.IndexOf("-of") + 1]);
    }

    [Fact]
    public void EveryAttributeTheReaderNeedsIsAskedFor()
    {
        Assert.All(
            new[] { "codec_type", "width", "height", "field_order", "r_frame_rate", "channels", "channel_layout" },
            entry => Assert.Contains(entry, FfprobeInvocation.Entries, StringComparison.Ordinal));
    }

    [Fact]
    public void TheSourceIsTheLastArgumentAndFollowsTheOneThatNamesAnInput()
    {
        string[] arguments = [.. FfprobeInvocation.Arguments(new StreamSource("/srv/recordings/k-1.ts"))];

        Assert.Equal("/srv/recordings/k-1.ts", arguments[^1]);
        Assert.Equal("-i", arguments[^2]);
    }

    [Fact]
    public void NothingButTheSourceVaries()
    {
        string[] one = [.. FfprobeInvocation.Arguments(new StreamSource("/srv/recordings/k-1.ts"))];
        string[] other = [.. FfprobeInvocation.Arguments(new StreamSource("/srv/recordings/k-2.ts"))];

        Assert.Equal(one[..^1], other[..^1]);
    }

    [Theory]
    [InlineData("-i")]
    [InlineData("-f")]
    [InlineData("--help")]
    public void ASourceThatWouldBeReadAsAnOptionIsRefused(string value)
    {
        Assert.Throws<ArgumentException>(() => new StreamSource(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ASourceThatNamesNothingIsRefused(string value)
    {
        Assert.Throws<ArgumentException>(() => new StreamSource(value));
    }

    [Fact]
    public void ASourceCarryingTextFromTheBroadcastIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new StreamSource("/srv/recordings/one\ntwo.ts"));
    }

    [Fact]
    public void APathThatMerelyLooksLikeProgrammeTextIsStillOneArgument()
    {
        string[] arguments = [.. FfprobeInvocation.Arguments(
            new StreamSource("/srv/recordings/k-1 ; rm -rf / ; .ts"))];

        Assert.Equal("/srv/recordings/k-1 ; rm -rf / ; .ts", arguments[^1]);
        Assert.Equal(9, arguments.Length);
    }
}
