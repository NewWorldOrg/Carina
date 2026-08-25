using System.Text.Json;
using System.Text.Json.Serialization;

using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Integrity;

public sealed class JsonIntegrityReportStore(IntegritySettings settings) : IIntegrityReportStore
{
    public const int Schema = 1;

    private const string WhileWriting = ".writing";

    private static readonly JsonSerializerOptions Shape = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    public async Task SaveAsync(IntegritySweep sweep, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sweep);

        string? holding = Path.GetDirectoryName(settings.ReportPath);

        if (!string.IsNullOrEmpty(holding))
        {
            Directory.CreateDirectory(holding);
        }

        string half = settings.ReportPath + WhileWriting;
        await File.WriteAllTextAsync(half, JsonSerializer.Serialize(Written(sweep), Shape), cancellationToken);
        File.Move(half, settings.ReportPath, overwrite: true);
    }

    public async Task<IntegritySweep?> LatestAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(settings.ReportPath))
        {
            return null;
        }

        string held = await File.ReadAllTextAsync(settings.ReportPath, cancellationToken);
        Document document = JsonSerializer.Deserialize<Document>(held, Shape)
            ?? throw new InvalidDataException($"{settings.ReportPath} holds no report.");

        if (document.Schema != Schema)
        {
            throw new InvalidDataException(
                $"{settings.ReportPath} was written against schema {document.Schema}, and this one reads {Schema}.");
        }

        return IntegritySweep.Of(
            document.RanAt,
            document.RootsWalked,
            document.RootsOutOfReach,
            document.FilesRead,
            document.LedgerRowsRead,
            document.LedgerRowsJudged,
            document.LedgerRowsStillWriting,
            document.LedgerRowsInRootsOutOfReach,
            [.. document.Findings.Select(Read)]);
    }

    private static Document Written(IntegritySweep sweep)
        => new(
            Schema,
            sweep.RanAt,
            sweep.RootsWalked,
            sweep.RootsOutOfReach,
            sweep.FilesRead,
            sweep.LedgerRowsRead,
            sweep.LedgerRowsJudged,
            sweep.LedgerRowsStillWriting,
            sweep.LedgerRowsInRootsOutOfReach,
            [.. sweep.Findings.Select(Written)]);

    private static Entry Written(IntegrityFinding finding)
        => new(
            finding.Fault.ToString(),
            finding.Root.Value,
            finding.FileName,
            finding.RecordingId?.Value,
            finding.LedgerSize,
            finding.ObservedSize,
            finding.NoticedAt);

    private static IntegrityFinding Read(Entry entry)
    {
        if (!Enum.TryParse(entry.Fault, out IntegrityFault fault) || !Enum.IsDefined(fault))
        {
            throw new InvalidDataException($"A report holds no class named '{entry.Fault}'.");
        }

        var root = new OutputRoot(entry.Root);
        DateTime noticedAt = entry.NoticedAt;

        if (fault is IntegrityFault.NoLedgerRow)
        {
            return IntegrityFinding.NoLedgerRow(root, entry.FileName, Observed(entry), noticedAt);
        }

        var recordingId = new RecordingId(
            entry.RecordingId ?? throw Incomplete(entry, nameof(Entry.RecordingId)));
        var fileName = new RecordingFileName(entry.FileName);
        long ledgerSize = entry.LedgerSize ?? throw Incomplete(entry, nameof(Entry.LedgerSize));

        return fault switch
        {
            IntegrityFault.SizeDisagrees => IntegrityFinding.SizeDisagrees(
                root,
                recordingId,
                fileName,
                ledgerSize,
                Observed(entry),
                noticedAt),
            IntegrityFault.FileMissing => IntegrityFinding.FileMissing(
                root,
                recordingId,
                fileName,
                ledgerSize,
                noticedAt),
            _ => IntegrityFinding.FileEmpty(
                root,
                recordingId,
                fileName,
                ledgerSize,
                Observed(entry),
                noticedAt),
        };
    }

    private static long Observed(Entry entry)
        => entry.ObservedSize ?? throw Incomplete(entry, nameof(Entry.ObservedSize));

    private static InvalidDataException Incomplete(Entry entry, string missing)
        => new($"A {entry.Fault} finding for '{entry.FileName}' says nothing about its {missing}.");

    private sealed record Document(
        int Schema,
        DateTime RanAt,
        int RootsWalked,
        int RootsOutOfReach,
        int FilesRead,
        int LedgerRowsRead,
        int LedgerRowsJudged,
        int LedgerRowsStillWriting,
        int LedgerRowsInRootsOutOfReach,
        IReadOnlyList<Entry> Findings);

    private sealed record Entry(
        string Fault,
        string Root,
        string FileName,
        Guid? RecordingId,
        long? LedgerSize,
        long? ObservedSize,
        DateTime NoticedAt);
}
