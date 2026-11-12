using Carina.Domain.Captions;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Tests.Recordings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeCaptionTrackDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly CaptionRecord WithText = new(1440, 1080, TimeSpan.Zero, [], [new CaptionLine(1, "字")]);

    private readonly HeldRecordingRows recordings = new();

    private readonly HeldCaptionRecords records = new();

    private EncodeCaptionTrackDomainService Service => new(recordings, records);

    [Fact(DisplayName = "BR-ED2-019: a recording whose captions were taken with their text has them carried into its artefact, as of when they were taken")]
    public async Task CaptionsTakenWithTheirTextAreCarried()
    {
        Recording recording = Captioned(CaptionState.Ready);
        records.Kept[recording.Id] = WithText;

        EncodeCaptions? carried = await Service.CaptionsToCarryAsync(recording.Id, Cancel);

        Assert.NotNull(carried);
        Assert.Same(WithText, carried.Record);
        Assert.Equal(recording.CaptionsMadeAt, carried.MadeAt);
    }

    [Fact(DisplayName = "BR-ED2-019: captions not taken yet, or taken without their text, leave the artefact with no text track")]
    public async Task CaptionsNotReadyOrWithoutTextAreNotCarried()
    {
        Recording pending = Ended();
        Recording failed = Captioned(CaptionState.Failed);
        Recording withoutText = Captioned(CaptionState.Ready);
        records.Kept[pending.Id] = WithText;
        records.Kept[failed.Id] = WithText;
        records.Kept[withoutText.Id] = new CaptionRecord(1440, 1080, TimeSpan.Zero, []);
        Recording unkept = Captioned(CaptionState.Ready);

        Assert.Null(await Service.CaptionsToCarryAsync(pending.Id, Cancel));
        Assert.Null(await Service.CaptionsToCarryAsync(failed.Id, Cancel));
        Assert.Null(await Service.CaptionsToCarryAsync(withoutText.Id, Cancel));
        Assert.Null(await Service.CaptionsToCarryAsync(unkept.Id, Cancel));
        Assert.Null(await Service.CaptionsToCarryAsync(RecordingId.New(), Cancel));
    }

    private Recording Captioned(CaptionState state)
    {
        Recording recording = Ended();
        recording.Caption(state, state is CaptionState.Ready ? 3 : null, RecordingFactory.Now.AddHours(2));

        return recording;
    }

    private Recording Ended()
    {
        Recording recording = RecordingFactory.Started();
        recording.Abort(RecordingFactory.Now.AddHours(1));
        recording.Settle(RecordingOutcome.Complete, 1_000_000, RecordingFactory.Now.AddHours(1));
        recordings.Rows.Add(recording);

        return recording;
    }

    private sealed class HeldRecordingRows : IRecordingRepository
    {
        public List<Recording> Rows { get; } = [];

        public Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken)
            => Task.FromResult(Rows.FirstOrDefault(row => row.Id.Equals(id)));

        public Task<IReadOnlyList<Recording>> ListInFlightAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Recording>> ListForReservationAsync(ReservationId reservationId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task AddAsync(Recording recording, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveAsync(Recording recording, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class HeldCaptionRecords : ICaptionRecords
    {
        public Dictionary<RecordingId, CaptionRecord> Kept { get; } = [];

        public Task<CaptionRecord?> ReadAsync(RecordingId id, CancellationToken cancellationToken)
            => Task.FromResult(Kept.GetValueOrDefault(id));

        public Task<TimeSpan?> StartsAtAsync(RecordingId id, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
