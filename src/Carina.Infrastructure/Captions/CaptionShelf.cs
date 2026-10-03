using Carina.Domain.Captions;
using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Captions;

/// <summary>
/// The directory the captions taken from recordings are kept in, one file a recording named after it.
/// A record is written beside its final name and moved over it, so a reader sees the old one or the new
/// one and never half of either.
/// </summary>
public sealed class CaptionShelf(CaptionSettings settings)
{
    public const string Unfinished = ".part";

    public async Task KeepAsync(RecordingId id, CaptionRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        string kept = Kept(id);
        string unfinished = kept + Unfinished;

        Directory.CreateDirectory(settings.WrittenTo!);
        await File.WriteAllBytesAsync(unfinished, CaptionRecordFormat.Written(record), cancellationToken);
        File.Move(unfinished, kept, overwrite: true);
    }

    public async Task<CaptionRecord?> ReadAsync(RecordingId id, CancellationToken cancellationToken)
    {
        string kept = Kept(id);

        if (!File.Exists(kept))
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

    public bool Holds(RecordingId id) => File.Exists(Kept(id));

    public void Forget(RecordingId id)
    {
        string kept = Kept(id);

        if (File.Exists(kept))
        {
            File.Delete(kept);
        }
    }

    private string Kept(RecordingId id)
        => settings.PathOf(id) ?? throw new InvalidOperationException("No directory is configured for captions.");
}
