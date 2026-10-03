using Carina.Domain.Captions;
using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Captions;

/// <summary>
/// The directory the captions taken from recordings are kept in, one file a recording named after it.
/// A record is written beside its final name and moved over it, so a reader sees the old one or the new
/// one and never half of either.
/// </summary>
public sealed class CaptionShelf(CaptionSettings settings) : ICaptionRecords
{
    public const string Unfinished = ".part";

    public async Task KeepAsync(RecordingId id, CaptionRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        string kept = Kept(id);
        string unfinished = kept + Unfinished;

        Directory.CreateDirectory(settings.WrittenTo!);

        try
        {
            await File.WriteAllBytesAsync(unfinished, CaptionRecordFormat.Written(record), cancellationToken);
            File.Move(unfinished, kept, overwrite: true);
        }
        catch
        {
            Unlink(unfinished);

            throw;
        }
    }

    public async Task<CaptionRecord?> ReadAsync(RecordingId id, CancellationToken cancellationToken)
    {
        if (settings.PathOf(id) is not { } kept || !File.Exists(kept))
        {
            return null;
        }

        try
        {
            return CaptionRecordFormat.Read(await File.ReadAllBytesAsync(kept, cancellationToken));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public async Task<TimeSpan?> StartsAtAsync(RecordingId id, CancellationToken cancellationToken)
    {
        if (settings.PathOf(id) is not { } kept || !File.Exists(kept))
        {
            return null;
        }

        byte[] head = new byte[CaptionRecordFormat.HeaderLength];

        try
        {
            await using FileStream reading = File.OpenRead(kept);
            int read = await reading.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);

            return CaptionRecordFormat.StartOf(head.AsSpan(0, read));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public bool Holds(RecordingId id) => File.Exists(Kept(id));

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

        return Directory.EnumerateFiles(shelf, "*" + CaptionSettings.Extension)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => name.EndsWith(CaptionSettings.Extension, StringComparison.Ordinal))
            .Select(name => name[..^CaptionSettings.Extension.Length])
            .ToHashSet(StringComparer.Ordinal);
    }

    private static void Unlink(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string Kept(RecordingId id)
        => settings.PathOf(id) ?? throw new InvalidOperationException("No directory is configured for captions.");
}
