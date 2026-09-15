namespace Carina.Architecture.Tests;

public sealed class FileErasureRuleSelfCheckTests
{
    [Fact]
    public void TheRuleSeesACallThatAsksTheDriverToEraseAFileOfEitherKind()
    {
        Assert.Equal(
            [".EraseStrayFileAsync(", ".EraseRecordingAsync("],
            FileErasureRules.AsksTheDriverToEraseIn(
                "await driver.EraseStrayFileAsync(request, token); await driver . EraseRecordingAsync (id, root, token);"));
    }

    [Fact]
    public void TheRuleDoesNotTakeTheDeclarationOnThePortForACall()
    {
        Assert.Empty(FileErasureRules.AsksTheDriverToEraseIn(
            "Task<DriverCall<StrayFileErasedDto>> EraseStrayFileAsync(StrayFileErasureRequest request);"));
    }

    [Theory]
    [InlineData("public sealed class LocalStrayEraser(ILogger logger) : IStrayFileEraser")]
    [InlineData("internal class Quiet : IDisposable, IRecordingFileEraser")]
    public void TheRuleSeesAnImplementationOfEitherPortHoweverItIsDeclared(string source)
    {
        Assert.True(FileErasureRules.ImplementsAnErasurePortIn(source));
    }

    [Fact]
    public void TheRuleDoesNotTakeAFieldOfThePortForAnImplementation()
    {
        Assert.False(FileErasureRules.ImplementsAnErasurePortIn("private readonly IStrayFileEraser strays;"));
    }

    [Fact]
    public void TheDiskRuleWouldSeeALocalDeleteOnTheWayAFindingIsThrownAway()
    {
        Assert.Equal(["File.Delete"], FileSystemRules.WhatCouldChangeWhatIsOnDiskIn("File.Delete(path);"));
    }
}
