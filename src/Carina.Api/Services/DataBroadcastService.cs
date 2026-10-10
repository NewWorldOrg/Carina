using System.Globalization;

using Carina.Api.Common;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Recordings;

namespace Carina.Api.Services;

/// <summary>
/// A module version of a recording's data broadcast with the tag it is answered under, or the tag alone where the
/// asker already holds it.
/// </summary>
public sealed record KeptModule(string ETag, ModuleVersion? Version);

public enum DataBroadcastFailure
{
    Coming = 1,

    None = 2,
}

/// <summary>
/// Places the data broadcast kept for a recording on the source a playback offer plays, the way the captions are
/// placed, and hands over one module version of it. Nothing here writes; a record the row says is made and the shelf
/// no longer holds reads as coming until the next pass takes it again.
/// </summary>
public sealed class DataBroadcastService(
    IRecordingDirectory recordings,
    IDataBroadcastRecords records,
    SourcePlacement placement,
    ILogger<DataBroadcastService> logger)
{
    public async Task<ServiceResult<DataBroadcastTimeline, DataBroadcastFailure>> TimelineAsync(
        RecordingId id,
        PlaybackOffer offer,
        TimeSpan from,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentOutOfRangeException.ThrowIfLessThan(from, TimeSpan.Zero);

        DataBroadcastStanding standing = await StandingAsync(id, cancellationToken);

        if (standing is not DataBroadcastStanding.Ready)
        {
            return Refused(id, standing);
        }

        if (await records.OutlineAsync(id, cancellationToken) is not { } record)
        {
            logger.LogWarning(
                "The data broadcast kept for recording {Recording} begins as a record of one and cannot be read as one.",
                id.Wire);

            return Refused(id, DataBroadcastStanding.None);
        }

        if (await placement.PlaceAsync(id, offer, record.Start, "data broadcast carousels", cancellationToken) is not { } zero)
        {
            return Refused(id, DataBroadcastStanding.None);
        }

        return ServiceResult<DataBroadcastTimeline, DataBroadcastFailure>.Success(
            DataBroadcastTimeline.Of(record, zero.Shift, zero.Length, from));
    }

    /// <summary>
    /// One module version of the record kept for a recording whose data broadcast is ready to play, with the tag that
    /// changes whenever the record is taken again. Where the asker already holds what that tag names, the version is
    /// not read and only the tag is answered.
    /// </summary>
    public async Task<ServiceResult<KeptModule, DataBroadcastFailure>> ModuleAsync(
        RecordingId id,
        ModuleVersionKey key,
        Func<string, bool> alreadyHeld,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alreadyHeld);

        if (await ReadyAsync(id, cancellationToken) is not { DataBroadcastMadeAt: { } madeAt }
            || records.BytesOf(id) is not { } bytes)
        {
            return NoSuchModule(id);
        }

        string tag = string.Create(CultureInfo.InvariantCulture, $"\"{madeAt.Ticks:x}-{bytes:x}\"");

        if (alreadyHeld(tag))
        {
            return ServiceResult<KeptModule, DataBroadcastFailure>.Success(new KeptModule(tag, null));
        }

        return await records.ModuleAsync(id, key, cancellationToken) is { } found
            ? ServiceResult<KeptModule, DataBroadcastFailure>.Success(new KeptModule(tag, found))
            : NoSuchModule(id);
    }

    private static ServiceResult<KeptModule, DataBroadcastFailure> NoSuchModule(RecordingId id)
        => ServiceResult<KeptModule, DataBroadcastFailure>.Failure(
            $"Recording {id.Wire} has no such version of a module of its data broadcast ready to play.",
            DataBroadcastFailure.None);

    private static ServiceResult<DataBroadcastTimeline, DataBroadcastFailure> Refused(RecordingId id, DataBroadcastStanding standing)
        => standing is DataBroadcastStanding.Coming
            ? ServiceResult<DataBroadcastTimeline, DataBroadcastFailure>.Failure(
                $"The data broadcast of recording {id.Wire} is still being taken from its file.",
                DataBroadcastFailure.Coming)
            : ServiceResult<DataBroadcastTimeline, DataBroadcastFailure>.Failure(
                $"Recording {id.Wire} has no data broadcast that can be played beside what it is played from.",
                DataBroadcastFailure.None);

    private async Task<DataBroadcastStanding> StandingAsync(RecordingId id, CancellationToken cancellationToken)
        => (await StandAsync(id, cancellationToken)).Standing;

    private async Task<Recording?> ReadyAsync(RecordingId id, CancellationToken cancellationToken)
        => await StandAsync(id, cancellationToken) is { Standing: DataBroadcastStanding.Ready, Recording: { } recording }
            ? recording
            : null;

    private async Task<(DataBroadcastStanding Standing, Recording? Recording)> StandAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (!records.KeepsAnything || await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return (DataBroadcastStanding.None, null);
        }

        DataBroadcastStanding standing = recording.DataBroadcast.StandingWith(
            recording.DataBroadcastState is DataBroadcastState.Made && records.Holds(id));

        return (standing, recording);
    }
}
