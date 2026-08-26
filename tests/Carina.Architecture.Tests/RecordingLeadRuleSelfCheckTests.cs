namespace Carina.Architecture.Tests;

public sealed class RecordingLeadRuleSelfCheckTests
{
    private const string Driver = """
        public sealed record DvbTunerSettings(TimeSpan LockPatience, TimeSpan RetryInterval, TimeSpan BytePatience)
        {
            public static readonly DvbTunerSettings Default = new(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromSeconds(5)
            );
        }
        """;

    private const string Sessions = """
        public static readonly TimeSpan HandOverLimit = TimeSpan.FromSeconds(10);
        """;

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

    [Fact]
    public void ADriverWhoseShapeTheRuleCannotReadIsReportedRatherThanPassed()
    {
        Assert.Single(Judged(Recorder(5, 5, 10), driver: "public sealed record DvbTunerSettings;"));
    }

    [Fact]
    public void ADriverThatNamesADifferentNumberOfWaitsIsReportedRatherThanPassed()
    {
        string third = Driver.Replace(
            "TimeSpan.FromMilliseconds(100)",
            "TimeSpan.FromSeconds(1)",
            StringComparison.Ordinal);

        Assert.Single(Judged(Recorder(5, 5, 10), driver: third));
    }

    private static IReadOnlyList<string> Judged(string recorder, string? driver = null)
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-head-");

        try
        {
            Write(directory, RecordingLeadRules.DriverSettings, driver ?? Driver);
            Write(directory, RecordingLeadRules.DriverSessions, Sessions);
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
