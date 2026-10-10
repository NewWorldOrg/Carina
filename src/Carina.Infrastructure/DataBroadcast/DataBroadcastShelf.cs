using Carina.Domain.Captions;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Captions;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// Where the data broadcast taken from recordings is kept: the directory the captions are kept in, one file a
/// recording named after it. A record is written beside its final name and moved over it, so a reader sees the
/// old one or the new one and never half of either.
/// </summary>
public sealed class DataBroadcastShelf(CaptionSettings settings) : IDataBroadcastRecords
{
    public const string Extension = ".databroadcast";

    public bool KeepsAnything => settings.WrittenTo is not null;

    public async Task KeepAsync(RecordingId id, DataBroadcastRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        string kept = Kept(id);
        string unfinished = kept + CaptionShelf.Unfinished;

        Directory.CreateDirectory(settings.WrittenTo!);

        try
        {
            await using (FileStream writing = File.Create(unfinished, 1 << 16, FileOptions.Asynchronous))
            {
                DataBroadcastRecordFormat.Write(record, writing);
                await writing.FlushAsync(cancellationToken);
                writing.Flush(flushToDisk: true);
            }

            File.Move(unfinished, kept, overwrite: true);
        }
        catch
        {
            Unlink(unfinished);

            throw;
        }
    }

    /// <summary>
    /// The record kept for a recording, or null when none is kept or what is kept is not a record.
    /// </summary>
    public async Task<DataBroadcastRecord?> ReadAsync(RecordingId id, CancellationToken cancellationToken)
    {
        if (PathOf(id) is not { } kept || !File.Exists(kept))
        {
            return null;
        }

        try
        {
            return DataBroadcastRecordFormat.Read(await File.ReadAllBytesAsync(kept, cancellationToken));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public async Task<ModuleVersion?> ModuleAsync(RecordingId id, ModuleVersionKey key, CancellationToken cancellationToken)
    {
        if (PathOf(id) is not { } kept || !File.Exists(kept))
        {
            return null;
        }

        try
        {
            await using FileStream reading = new(kept, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 12, FileOptions.Asynchronous);

            return await DataBroadcastRecordFormat.FindAsync(reading, key, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a record is kept for the recording: a file under its name whose head is the head of a record.
    /// </summary>
    public bool Holds(RecordingId id) => PathOf(id) is { } kept && HeadsARecord(kept);

    public void Forget(RecordingId id) => Unlink(Kept(id));

    /// <summary>
    /// The recordings a record is kept for: the names on the shelf whose file begins as a record does, so that an
    /// empty or broken file is no record.
    /// </summary>
    public IReadOnlySet<string> Shelved()
    {
        if (settings.WrittenTo is not { } shelf || !Directory.Exists(shelf))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return Directory.EnumerateFiles(shelf, "*" + Extension)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => name.EndsWith(Extension, StringComparison.Ordinal) && HeadsARecord(Path.Combine(shelf, name)))
            .Select(name => name[..^Extension.Length])
            .ToHashSet(StringComparer.Ordinal);
    }

    public string? PathOf(RecordingId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return settings.WrittenTo is { } shelf ? Path.Combine(shelf, id.Wire + Extension) : null;
    }

    private static bool HeadsARecord(string kept)
    {
        byte[] head = new byte[DataBroadcastRecordFormat.HeaderLength];

        try
        {
            using FileStream reading = File.OpenRead(kept);
            int read = reading.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);

            return DataBroadcastRecordFormat.Heads(head.AsSpan(0, read));
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Unlink(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string Kept(RecordingId id)
        => PathOf(id) ?? throw new InvalidOperationException("No directory is configured for the records taken from recordings.");
}
