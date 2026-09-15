namespace Carina.Architecture.Tests;

public sealed class RecordingRetryRuleSelfCheckTests
{
    private const string Weighing = """
        public sealed class RecordingRetries
        {
            public RetryVerdict Weigh(RetrySighting sighting) => StartRetry.For(sighting, RetryPolicy.Default, now);
        }
        """;

    [Theory]
    [InlineData("loaded.Settle(RecordingOutcome.Failed, 0, now);")]
    [InlineData("loaded . Settle (outcome, weighed, now);")]
    [InlineData("Recording recording = Recording.Begin(id, reservation, programme);")]
    [InlineData("Recording.Rehydrate(id, reservation);")]
    [InlineData("private readonly IRecordingRepository recordings;")]
    [InlineData("private readonly IRecordingDirectory directory;")]
    [InlineData("await contract.ClaimAsync(id, now, token); IReservationRecordingContract contract;")]
    [InlineData("await driver.StartSessionAsync(request, token);")]
    public void AReachForARecordingInAFileOfTheRetryIsReported(string reach)
        => Assert.Equal(
            ["/Carina.Infrastructure/Recordings/RecordingRetries.cs"],
            Judged(("/Carina.Infrastructure/Recordings/RecordingRetries.cs", Weighing + reach)));

    [Fact]
    public void AFileOfTheRetryThatOnlyWeighsIsPassed()
        => Assert.Empty(Judged(("/Carina.Infrastructure/Recordings/RecordingRetries.cs", Weighing)));

    [Fact]
    public void ASettleOutsideTheRetryIsNotWhatThisRuleReads()
        => Assert.Empty(Judged(
            ("/Carina.Infrastructure/Recordings/RecordingStreamSupervisor.cs", "loaded.Settle(outcome, 0, now);"),
            ("/Carina.Infrastructure/Recordings/RecordingRetries.cs", Weighing)));

    [Fact]
    public void AnyFileNamedForTheRetryIsCountedAsPartOfItWhereverItSits()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-retry-");

        try
        {
            Write(directory, "/Carina.Api/Services/RetryEverything.cs", "Recording.Begin(");
            Write(directory, "/Carina.Domain/Recordings/RecordingRetries.cs", Weighing);
            Write(directory, "/Carina.Domain/Recordings/Recording.cs", "public void Settle(");

            Assert.Equal(
                ["/Carina.Api/Services/RetryEverything.cs", "/Carina.Domain/Recordings/RecordingRetries.cs"],
                RecordingRetryRules.FilesOfTheRetry(directory.FullName));
            Assert.Equal(
                ["/Carina.Api/Services/RetryEverything.cs"],
                RecordingRetryRules.RetryFilesThatReachARecordingOfTheirOwn(directory.FullName));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void BuildOutputAndMigrationsAreNotRead()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-retry-");

        try
        {
            Write(directory, "/Carina.Infrastructure/obj/Debug/RecordingRetries.cs", "loaded.Settle(");
            Write(directory, "/Carina.Infrastructure/bin/Debug/RecordingRetries.cs", "loaded.Settle(");
            Write(directory, "/Carina.Db/Migrations/20260101000000_RetryLedger.cs", "IRecordingRepository");

            Assert.Empty(RecordingRetryRules.FilesOfTheRetry(directory.FullName));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static IReadOnlyList<string> Judged(params (string Relative, string Source)[] files)
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-retry-");

        try
        {
            foreach ((string relative, string source) in files)
            {
                Write(directory, relative, source);
            }

            return RecordingRetryRules.RetryFilesThatReachARecordingOfTheirOwn(directory.FullName);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static void Write(DirectoryInfo directory, string relative, string source)
    {
        string path = Path.Combine(directory.FullName, relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
    }
}
