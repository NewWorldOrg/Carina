using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;

using Carina.Domain.Migration;
using Carina.Infrastructure.Migration;
using Carina.TestSupport;

using static Carina.Infrastructure.Tests.Migration.CarriedMigrationFixtures;

namespace Carina.Infrastructure.Tests.Migration;

internal static class MigrationOnRealFiles
{
    public const string Carried = "first.m2ts";

    public const string AlsoCarried = "second.m2ts";

    public const string NotARecording = "notes.txt";

    public const string NothingLandedIn = "landed-nothing.m2ts";

    public const long CarriedSize = 4096;

    public const long AlsoCarriedSize = 8192;

    public static void LayDown(string directory)
    {
        Write(directory, Carried, CarriedSize);
        Write(directory, AlsoCarried, AlsoCarriedSize);
        Write(directory, NotARecording, 1024);
        Write(directory, NothingLandedIn, 0);
    }

    private static void Write(string directory, string name, long size)
    {
        byte[] filling = new byte[size];

        for (int at = 0; at < filling.Length; at++)
        {
            filling[at] = (byte)((name[0] + at) % 256);
        }

        File.WriteAllBytes(Path.Combine(directory, name), filling);
    }
}

internal sealed class ASourceOfRealFiles(string directory) : IMigrationSourceLedger, IMigrationSourceDirectory
{
    public int Walks { get; private set; }

    public Exception? WhenWalking { get; set; }

    public Task<SourceLedger> ReadAsync(CancellationToken cancellationToken)
        => Task.FromResult(Ledger(
            [Recording(1), Recording(2), Recording(3)],
            [
                AsBroadcast(1, MigrationOnRealFiles.Carried, MigrationOnRealFiles.CarriedSize),
                AsBroadcast(2, MigrationOnRealFiles.AlsoCarried, MigrationOnRealFiles.AlsoCarriedSize),
                AsBroadcast(3, MigrationOnRealFiles.NothingLandedIn, 2048),
            ],
            [Rule(1)]));

    public Task<IReadOnlyList<SourceFile>> ListAsync(CancellationToken cancellationToken)
    {
        Walks++;

        if (WhenWalking is { } refused)
        {
            throw refused;
        }

        return Task.FromResult<IReadOnlyList<SourceFile>>(
            [.. Directory
                .GetFiles(directory)
                .Order(StringComparer.Ordinal)
                .Select(file => OnDisk(Path.GetFileName(file), new FileInfo(file).Length))]);
    }
}

internal sealed record FileStanding(
    string Name,
    long Inode,
    long Links,
    long Size,
    long Blocks,
    long BlockSize,
    string Digest,
    DateTime WrittenAt)
{
    public string Identity => string.Create(
        CultureInfo.InvariantCulture,
        $"{Name} inode {Inode} {Size} bytes {Digest} written {WrittenAt:O}");
}

internal static class WhatTheFilesystemSays
{
    private const string Stat = "/usr/bin/stat";

    public static IReadOnlyList<FileStanding> Under(string directory)
    {
        string[] found =
        [
            .. Directory
                .GetFiles(directory, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal),
        ];

        IReadOnlyList<string> said = Asked(found);
        List<FileStanding> standing = [];

        for (int at = 0; at < found.Length; at++)
        {
            string[] fields = said[at].Split('\t');

            Assert.Equal(found[at], fields[0]);

            standing.Add(new FileStanding(
                Path.GetRelativePath(directory, found[at]),
                Number(fields[1]),
                Number(fields[2]),
                Number(fields[3]),
                Number(fields[4]),
                Number(fields[5]),
                Digested(found[at]),
                File.GetLastWriteTimeUtc(found[at])));
        }

        return standing;
    }

    public static IReadOnlyList<string> Identities(IReadOnlyList<FileStanding> standing)
        => [.. standing.Select(file => file.Identity)];

    public static long BytesTheDiskHoldsFor(params IReadOnlyList<FileStanding>[] places)
    {
        ArgumentNullException.ThrowIfNull(places);

        return places
            .SelectMany(place => place)
            .DistinctBy(file => file.Inode)
            .Sum(file => file.Blocks * file.BlockSize);
    }

    public static FileStanding Named(IReadOnlyList<FileStanding> standing, string name)
        => standing.Single(file => string.Equals(file.Name, name, StringComparison.Ordinal));

    private static long Number(string said) => long.Parse(said, CultureInfo.InvariantCulture);

    private static string Digested(string path)
        => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static IReadOnlyList<string> Asked(IReadOnlyList<string> paths)
    {
        if (paths.Count is 0)
        {
            return [];
        }

        Assert.True(
            File.Exists(Stat),
            $"These tests measure inodes, link counts and allocated blocks, and {Stat} is what they ask.");

        ProcessStartInfo asking = new(Stat)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        asking.ArgumentList.Add("--printf");
        asking.ArgumentList.Add("%n\\t%i\\t%h\\t%s\\t%b\\t%B\\n");

        foreach (string path in paths)
        {
            asking.ArgumentList.Add(path);
        }

        using Process asked = Process.Start(asking)
            ?? throw new InvalidOperationException($"{Stat} did not start.");

        string answered = asked.StandardOutput.ReadToEnd();
        string complained = asked.StandardError.ReadToEnd();
        asked.WaitForExit();

        Assert.True(asked.ExitCode is 0, $"{Stat} refused: {complained}");

        return [.. answered.Split('\n', StringSplitOptions.RemoveEmptyEntries)];
    }
}

internal sealed class ARunOverRealFiles : IDisposable
{
    public ARunOverRealFiles()
    {
        From = Directory.CreateTempSubdirectory("carina-migration-source").FullName;
        Into = Directory.CreateTempSubdirectory("carina-migration-new-root").FullName;
        MigrationOnRealFiles.LayDown(From);
        Bench = new MigrationBench(Clock);
        Recordings = new HeldMigratedRecordings(Bench.Journal);
        Source = new ASourceOfRealFiles(From);
    }

    public string From { get; }

    public string Into { get; }

    public HandTurnedClock Clock { get; } = new(new DateTimeOffset(2026, 9, 8, 5, 0, 0, TimeSpan.Zero));

    public MigrationBench Bench { get; }

    public HeldMigratedRecordings Recordings { get; }

    public HeldMigrationRecords Records { get; } = new();

    public OneAtATime Lease { get; } = new();

    public ASourceOfRealFiles Source { get; }

    public Task<MigrationRunId> RunAsync(MigrationPass pass)
        => new MigrationPassage(
            Source,
            Source,
            Bench.Carriage(new HardLinkMigrationCarrier(From, Into, Root), Recordings),
            Records,
            Lease,
            Clock).RunAsync(pass, Rescanned(), CancellationToken.None);

    public void Dispose()
    {
        Directory.Delete(From, recursive: true);
        Directory.Delete(Into, recursive: true);
    }
}
