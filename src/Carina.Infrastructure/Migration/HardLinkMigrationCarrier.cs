using System.Runtime.InteropServices;

using Carina.Domain.Migration;
using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Migration;

public sealed class HardLinkMigrationCarrier : IMigrationCarrier
{
    private const int NoSuchEntry = 2;

    private const int AlreadyExists = 17;

    private const int AcrossDevices = 18;

    private readonly string from;

    private readonly string into;

    public HardLinkMigrationCarrier(string from, string into, OutputRoot root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(into);
        ArgumentNullException.ThrowIfNull(root);

        this.from = Path.TrimEndingDirectorySeparator(Path.GetFullPath(from));
        this.into = Path.TrimEndingDirectorySeparator(Path.GetFullPath(into));
        Into = root;
    }

    public OutputRoot Into { get; }

    public Task<MigrationRootStanding> StandingAsync(CancellationToken cancellationToken)
        => Task.FromResult(Directory.Exists(into)
            ? Directory.EnumerateFileSystemEntries(into).Any()
                ? MigrationRootStanding.NotEmpty
                : MigrationRootStanding.Empty
            : MigrationRootStanding.Missing);

    public Task<MigrationCarry> CarryAsync(
        string sourcePath,
        RecordingFileName name,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        string existing = Path.GetFullPath(Path.Combine(from, SourcePath.Of(sourcePath, nameof(sourcePath))));

        if (!RecordingFilePlace.LiesDirectlyUnder(from, existing))
        {
            return Task.FromResult(new MigrationCarry(
                MigrationCarryOutcome.Refused,
                $"'{sourcePath}' is not in the source output directory, and nothing outside it is carried."));
        }

        if (!Directory.Exists(into))
        {
            return Task.FromResult(new MigrationCarry(
                MigrationCarryOutcome.Refused,
                $"The new root '{into}' is not there, and a migration makes no room of its own."));
        }

        return Task.FromResult(Link(existing, Path.Combine(into, name.Value)) is 0
            ? MigrationCarry.Linked()
            : Read(Marshal.GetLastPInvokeError()));
    }

    public static MigrationCarry Read(int errorNumber)
        => errorNumber switch
        {
            NoSuchEntry => new MigrationCarry(
                MigrationCarryOutcome.SourceGone,
                "The file the source ledger names is not on the disk."),
            AlreadyExists => new MigrationCarry(
                MigrationCarryOutcome.AlreadyThere,
                "Something is already there under that name, and a run that starts again starts from an empty new root."),
            AcrossDevices => new MigrationCarry(
                MigrationCarryOutcome.NotOnTheSameFilesystem,
                "A hard link cannot cross filesystems, so the new root sits on the same filesystem as the source. "
                + "Nothing was copied."),
            _ => new MigrationCarry(
                MigrationCarryOutcome.Refused,
                Marshal.GetPInvokeErrorMessage(errorNumber)),
        };

    [DllImport("libc", EntryPoint = "link", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern int Link(string existing, string made);
}
