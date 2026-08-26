using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;

namespace Carina.Domain.Tests.Thumbnails;

public sealed class ThumbnailRenderTests
{
    [Fact]
    public void APictureThatWasDrawnNamesNoFaultAndNoCode()
    {
        ThumbnailRender drawn = ThumbnailRender.Drawn();

        Assert.True(drawn.Drew);
        Assert.Null(drawn.Fault);
        Assert.Null(drawn.ExitCode);
        Assert.Equal(string.Empty, drawn.Note);
    }

    [Fact]
    public void AProgrammeThatRefusedCarriesTheCodeItRefusedWith()
    {
        ThumbnailRender refused = ThumbnailRender.Refused(234, "  Invalid data found  ");

        Assert.False(refused.Drew);
        Assert.Equal(ThumbnailFault.Refused, refused.Fault);
        Assert.Equal(234, refused.ExitCode);
        Assert.Equal("Invalid data found", refused.Note);
    }

    [Fact]
    public void AProgrammeThatExitedWellIsNotOneThatRefused()
        => Assert.Equal(
            "exitCode",
            Assert.Throws<ArgumentOutOfRangeException>(() => ThumbnailRender.Refused(0, "fine")).ParamName);

    [Theory]
    [InlineData(ThumbnailFault.ProgrammeMissing)]
    [InlineData(ThumbnailFault.SourceOutOfReach)]
    [InlineData(ThumbnailFault.TimedOut)]
    [InlineData(ThumbnailFault.NothingWasWritten)]
    public void TheOtherWaysItCanGoWrongCarryNoCode(ThumbnailFault fault)
    {
        ThumbnailRender failed = ThumbnailRender.Failed(fault, "what happened");

        Assert.False(failed.Drew);
        Assert.Equal(fault, failed.Fault);
        Assert.Null(failed.ExitCode);
        Assert.Equal("what happened", failed.Note);
    }

    [Fact]
    public void RefusalIsNotOneOfThoseBecauseItHasACodeToCarry()
        => Assert.Equal(
            "fault",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ThumbnailRender.Failed(ThumbnailFault.Refused, "no code here")).ParamName);

    [Fact]
    public void AFaultTheLedgerDoesNotHoldIsRefused()
        => Assert.Equal(
            "fault",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ThumbnailRender.Failed((ThumbnailFault)99, "unknown")).ParamName);

    [Fact]
    public void ANoteIsKeptWholeUpToFiveHundredCharacters()
        => Assert.Equal(500, ThumbnailRender.LongestNote);

    [Theory]
    [InlineData(499)]
    [InlineData(500)]
    public void ANoteThatFitsIsKeptWhole(int length)
    {
        string complaint = new('x', length);

        Assert.Equal(complaint, ThumbnailRender.Failed(ThumbnailFault.TimedOut, complaint).Note);
    }

    [Fact]
    public void ALongerNoteKeepsItsEndBecauseThatIsWhereTheReasonIs()
    {
        string complaint = new string('x', 500) + "the last line";

        string kept = ThumbnailRender.Failed(ThumbnailFault.TimedOut, complaint).Note;

        Assert.Equal(500, kept.Length);
        Assert.EndsWith("the last line", kept, StringComparison.Ordinal);
    }

    [Fact]
    public void ARenderWithoutANoteIsRefused()
    {
        Assert.Equal(
            "note",
            Assert.Throws<ArgumentNullException>(
                () => ThumbnailRender.Failed(ThumbnailFault.TimedOut, null!)).ParamName);
        Assert.Equal(
            "note",
            Assert.Throws<ArgumentNullException>(() => ThumbnailRender.Refused(1, null!)).ParamName);
    }
}
