using Carina.Api.Common;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Recordings;

namespace Carina.Api.Services;

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

        if (await records.ReadAsync(id, cancellationToken) is not { } record)
        {
            logger.LogWarning(
                "The data broadcast kept for recording {Recording} begins as a record of one and cannot be read as one.",
                id.Wire);

            return Refused(id, DataBroadcastStanding.None);
        }

        if (await placement.PlaceAsync(id, offer, record.Start, "data broadcast", cancellationToken) is not { } zero)
        {
            return Refused(id, DataBroadcastStanding.None);
        }

        return ServiceResult<DataBroadcastTimeline, DataBroadcastFailure>.Success(
            DataBroadcastTimeline.Of(record, zero.Shift, zero.Length, from));
    }

    /// <summary>
    /// One module version of the record kept for a recording whose data broadcast is ready to play.
    /// </summary>
    public async Task<ServiceResult<ModuleVersion, DataBroadcastFailure>> ModuleAsync(
        RecordingId id,
        ModuleVersionKey key,
        CancellationToken cancellationToken)
    {
        if (await StandingAsync(id, cancellationToken) is DataBroadcastStanding.Ready
            && await records.ModuleAsync(id, key, cancellationToken) is { } found)
        {
            return ServiceResult<ModuleVersion, DataBroadcastFailure>.Success(found);
        }

        return ServiceResult<ModuleVersion, DataBroadcastFailure>.Failure(
            $"Recording {id.Wire} has no such version of a module of its data broadcast ready to play.",
            DataBroadcastFailure.None);
    }

    private static ServiceResult<DataBroadcastTimeline, DataBroadcastFailure> Refused(RecordingId id, DataBroadcastStanding standing)
        => standing is DataBroadcastStanding.Coming
            ? ServiceResult<DataBroadcastTimeline, DataBroadcastFailure>.Failure(
                $"The data broadcast of recording {id.Wire} is still being taken from its file.",
                DataBroadcastFailure.Coming)
            : ServiceResult<DataBroadcastTimeline, DataBroadcastFailure>.Failure(
                $"Recording {id.Wire} has no data broadcast that can be played beside what it is played from.",
                DataBroadcastFailure.None);

    private async Task<DataBroadcastStanding> StandingAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (!records.KeepsAnything || await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return DataBroadcastStanding.None;
        }

        return recording.DataBroadcast.StandingWith(
            recording.DataBroadcastState is DataBroadcastState.Made && records.Holds(id));
    }
}
