using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Integrity;
using Carina.Infrastructure.Tests.Scanning;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Integrity;

public sealed class NamesTheDiskAllowsTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 7, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly string[] Names =
    [
        "2026..08.m2ts",
        "..hidden.m2ts",
        "trailing.m2ts ",
        " leading.m2ts",
        "a\\b.m2ts",
        "-",
        "あ.m2ts",
        "a b  c.m2ts",
        "one'quote.m2ts",
        "one\"quote.m2ts",
        "percent%20.m2ts",
        ".hidden",
    ];

    public static TheoryData<string> Awkward
    {
        get
        {
            var awkward = new TheoryData<string>();

            foreach (string name in Names)
            {
                awkward.Add(name);
            }

            return awkward;
        }
    }

    [Theory]
    [MemberData(nameof(Awkward))]
    public async Task ANameTheDiskAllowsIsReadAndCarriedAsItIs(string name)
    {
        using var tree = new TempTree();
        tree.Holding(name, 12);

        RootListing listing = await Survey(tree).ListAsync(Primary, Cancel);

        Assert.True(listing.Reachable);
        Assert.Equal([name], listing.Files.Select(file => file.Path).ToArray());
        Assert.Equal(12, listing.At(name)?.SizeBytes);
    }

    [Fact]
    public async Task APathFarLongerThanAnyLedgerNameIsRead()
    {
        using var tree = new TempTree();
        string deep = string.Join("/", Enumerable.Repeat(new string('a', 200), 6));
        tree.Holding(deep, 3);

        RootListing listing = await Survey(tree).ListAsync(Primary, Cancel);

        Assert.Equal([deep], listing.Files.Select(file => file.Path).ToArray());
        Assert.True(deep.Length > 1024);
    }

    [Fact]
    public async Task ABackslashInANameIsNotADirectorySeparator()
    {
        using var tree = new TempTree();
        tree.Holding("a\\b.m2ts", 1).Holding("a/b.m2ts", 2);

        RootListing listing = await Survey(tree).ListAsync(Primary, Cancel);

        Assert.Equal(
            ["a/b.m2ts", "a\\b.m2ts"],
            listing.Files.Select(file => file.Path).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(1, listing.At("a\\b.m2ts")?.SizeBytes);
        Assert.Equal(2, listing.At("a/b.m2ts")?.SizeBytes);
    }

    [Fact]
    public async Task ASweepOverEveryAwkwardNameAtOnceStillFinishes()
    {
        using var tree = new TempTree();

        foreach (string name in Names)
        {
            tree.Holding(name, 12);
        }

        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(tree, checks, new StoppedClock(Now));

        IntegrityRun run = await job.RunAsync(Cancel);

        IntegrityReport swept = Assert.IsType<IntegrityReport>(run.Swept);

        Assert.Equal(Names.Length, swept.Check.FilesRead);
        Assert.Equal(Names.Length, swept.Findings.Count);
        Assert.All(swept.Findings, finding => Assert.Equal(IntegrityFault.NoLedgerRow, finding.Fault));
        Assert.Single(checks.Saved);
    }

    [Fact]
    public async Task TheLoopKeepsWritingCheckRowsWhileAnAwkwardNameSitsInTheRoot()
    {
        using var tree = new TempTree();
        tree.Holding("2026..08.m2ts", 12).Holding("trailing.m2ts ", 12).Holding("a\\b.m2ts", 12);

        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(tree, checks, new HurriedClock());
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(
            () => checks.Saved.Count >= 3,
            "the loop stopped writing check rows while an awkward name sat in the root");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.All(checks.Saved, saved => Assert.Equal(3, saved.Check.FilesRead));
    }

    [Fact]
    public void AWalkReachesIntoSubdirectories()
    {
        Assert.True(LocalRecordingFileSurvey.HowItWalks.RecurseSubdirectories);
    }

    [Fact]
    public void AWalkRefusesToPassOverWhatItCannotRead()
    {
        Assert.False(LocalRecordingFileSurvey.HowItWalks.IgnoreInaccessible);
    }

    [Fact]
    public void AWalkStepsOverLinksAndNothingElse()
    {
        Assert.Equal(FileAttributes.ReparsePoint, LocalRecordingFileSurvey.HowItWalks.AttributesToSkip);
    }

    private static LocalRecordingFileSurvey Survey(TempTree tree)
        => new(
            new IntegritySettings { OutputRoots = [new StorageRootPath(Primary, tree.Root)] },
            NullLogger<LocalRecordingFileSurvey>.Instance);

    private static IntegrityCheckJob Job(TempTree tree, HeldChecks checks, TimeProvider clock)
    {
        var settings = new IntegritySettings
        {
            OutputRoots = [new StorageRootPath(Primary, tree.Root)],
            BeforeFirstSweep = TimeSpan.FromMinutes(1),
            BetweenSweeps = TimeSpan.FromMinutes(1),
        };

        var services = new ServiceCollection();
        services.AddScoped<IRecordingLedger>(_ => new HeldLedger());
        services.AddScoped<IIntegrityCheckRepository>(_ => checks);

        return new IntegrityCheckJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new LocalRecordingFileSurvey(settings, NullLogger<LocalRecordingFileSurvey>.Instance),
            settings,
            clock,
            NullLogger<IntegrityCheckJob>.Instance);
    }
}
