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
public sealed class DataBroadcastShelf(CaptionSettings settings)
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

    public bool Holds(RecordingId id) => PathOf(id) is { } kept && File.Exists(kept);

    public void Forget(RecordingId id) => Unlink(Kept(id));

    /// <summary>
    /// The recordings a record is kept for, read off the names on the shelf.
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
            .Where(name => name.EndsWith(Extension, StringComparison.Ordinal))
            .Select(name => name[..^Extension.Length])
            .ToHashSet(StringComparer.Ordinal);
    }

    public string? PathOf(RecordingId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return settings.WrittenTo is { } shelf ? Path.Combine(shelf, id.Wire + Extension) : null;
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
