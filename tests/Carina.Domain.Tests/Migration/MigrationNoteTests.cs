using Carina.Domain.Migration;

namespace Carina.Domain.Tests.Migration;

public sealed class MigrationNoteTests
{
    [Fact]
    public void AProgrammeNameLosesItsLineBreaks()
    {
        Assert.Equal("onetwo", MigrationNote.Of("one\r\ntwo"));
    }

    [Fact]
    public void AProgrammeNameLosesTheCharactersThatDriveATerminal()
    {
        Assert.Equal("[31mone", MigrationNote.Of("\u001b[31mone"));
    }

    [Fact]
    public void AProgrammeNameKeepsTheLettersItWasBroadcastWith()
    {
        Assert.Equal("[字]ニュース", MigrationNote.Of("[字]ニュース"));
    }

    [Fact]
    public void AProgrammeNameIsTrimmed()
    {
        Assert.Equal("one", MigrationNote.Of("  one  "));
    }

    [Fact]
    public void AProgrammeNameNoLongerThanTheRecordHoldsIsKeptWhole()
    {
        string said = new('a', MigrationNote.Longest);

        Assert.Equal(said, MigrationNote.Of(said));
    }

    [Fact]
    public void AProgrammeNameLongerThanTheRecordHoldsIsCutAtTheEnd()
    {
        string said = new('a', MigrationNote.Longest + 10);

        Assert.Equal(MigrationNote.Longest, MigrationNote.Of(said).Length);
    }

    [Fact]
    public void ASourceThatNamesItselfWithNothingReadableIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new MigrationSourceName(" "));
    }

    [Fact]
    public void ASourceNameLosesTheCharactersThatDriveATerminal()
    {
        Assert.Equal("a system", new MigrationSourceName("a system\u0007").Value);
    }

    [Fact]
    public void ASourceNameIsNotEndless()
    {
        Assert.Throws<ArgumentException>(
            () => new MigrationSourceName(new string('a', MigrationSourceName.MaxLength + 1)));
    }
}
