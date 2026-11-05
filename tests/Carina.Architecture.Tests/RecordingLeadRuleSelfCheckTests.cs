namespace Carina.Architecture.Tests;

public sealed class RecordingLeadRuleSelfCheckTests
{
    [Theory]
    [InlineData(5, 5, 10, 0)]
    [InlineData(4, 5, 10, 1)]
    [InlineData(5, 4, 10, 1)]
    [InlineData(5, 5, 9, 1)]
    [InlineData(4, 4, 9, 3)]
    public void AHeadThatDoesNotAddUpToWhatTheDriverSpendsIsReported(
        int lockSeconds,
        int byteSeconds,
        int seatSeconds,
        int expected)
    {
        Assert.Equal(expected, Judged(Recorder(lockSeconds, byteSeconds, seatSeconds)).Count);
    }

    [Fact]
    public void ARecorderThatStoppedNamingItsPartsIsReportedRatherThanPassed()
    {
        Assert.Equal(3, Judged("public static readonly TimeSpan TuningLead = TimeSpan.FromSeconds(25);").Count);
    }

    private static IReadOnlyList<string> Judged(string recorder)
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-head-");

        try
        {
            Write(directory, RecordingLeadRules.RecorderSettings, recorder);

            return RecordingLeadRules.WhereTheHeadDisagreesWithTheDriver(directory.FullName);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static string Recorder(int lockSeconds, int byteSeconds, int seatSeconds)
        => $"""
            public static readonly TimeSpan WaitingForASeat = TimeSpan.FromSeconds({seatSeconds});
            public static readonly TimeSpan WaitingForALock = TimeSpan.FromSeconds({lockSeconds});
            public static readonly TimeSpan WaitingForTheFirstByte = TimeSpan.FromSeconds({byteSeconds});
            """;

    private static void Write(DirectoryInfo directory, string relative, string source)
    {
        string path = Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
    }
}
