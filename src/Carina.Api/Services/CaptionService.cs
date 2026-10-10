using Carina.Api.Common;
using Carina.Domain.Captions;
using Carina.Domain.Recordings;

namespace Carina.Api.Services;

public enum CaptionFailure
{
    Coming = 1,

    None = 2,
}

/// <summary>
/// Places the captions kept for a recording on the source a playback offer plays: the recording itself
/// counts its seconds from where the file's own clock begins, an artefact from where the job that made it
/// began reading plus what it skipped at the head. Nothing here writes; a record the row says is ready and
/// the shelf no longer holds reads as coming until the caption pass takes it again.
/// </summary>
public sealed class CaptionService(
    IRecordingDirectory recordings,
    ICaptionRecords records,
    CaptionSettings settings,
    SourcePlacement placement,
    ILogger<CaptionService> logger)
{
    public async Task<ServiceResult<CaptionStanding>> StandingAsync(
        RecordingId id,
        PlaybackOffer offer,
        CancellationToken cancellationToken)
        => ServiceResult<CaptionStanding>.Success((await PlacedAsync(id, offer, cancellationToken)).Standing);

    public async Task<ServiceResult<CaptionWindow, CaptionFailure>> WindowAsync(
        RecordingId id,
        PlaybackOffer offer,
        TimeSpan from,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(from, TimeSpan.Zero);

        Placing placing = await PlacedAsync(id, offer, cancellationToken);

        if (placing is not { Standing: CaptionStanding.Ready, Zero: { } zero })
        {
            return Refused(id, placing.Standing);
        }

        if (await records.ReadAsync(id, cancellationToken) is not { } record)
        {
            logger.LogWarning(
                "The captions kept for recording {Recording} begin as a record of captions and cannot be read as one.",
                id.Wire);

            return Refused(id, CaptionStanding.None);
        }

        return ServiceResult<CaptionWindow, CaptionFailure>.Success(CaptionWindow.Of(record, zero.Shift, zero.Length, from));
    }

    private static ServiceResult<CaptionWindow, CaptionFailure> Refused(RecordingId id, CaptionStanding standing)
        => standing is CaptionStanding.Coming
            ? ServiceResult<CaptionWindow, CaptionFailure>.Failure(
                $"The captions of recording {id.Wire} are still being taken from its file.",
                CaptionFailure.Coming)
            : ServiceResult<CaptionWindow, CaptionFailure>.Failure(
                $"Recording {id.Wire} has no captions that can be drawn over what it is played from.",
                CaptionFailure.None);

    private async Task<Placing> PlacedAsync(RecordingId id, PlaybackOffer offer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(offer);

        if (!settings.KeepsAnything || await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return Placing.Nowhere;
        }

        TimeSpan? startsAt = recording.CaptionState is CaptionState.Ready
            ? await records.StartsAtAsync(id, cancellationToken)
            : null;
        CaptionStanding standing = CaptionLedger.StandingOf(recording, startsAt is not null);

        if (standing is not CaptionStanding.Ready || startsAt is not { } begins)
        {
            return new Placing(standing, null);
        }

        return await placement.PlaceAsync(id, offer, begins, "captions", cancellationToken) is { } zero
            ? new Placing(CaptionStanding.Ready, zero)
            : Placing.Nowhere;
    }

    private sealed record Placing(CaptionStanding Standing, SourceZero? Zero)
    {
        public static readonly Placing Nowhere = new(CaptionStanding.None, null);
    }
}
