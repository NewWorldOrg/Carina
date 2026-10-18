using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class LearningSelectionTests
{
    private static readonly DateTime Monday = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-CD-004: a round reads the confirmed recordings first, then those answered and not confirmed, then the rest, the newest first within each")]
    public void ConfirmedRecordingsComeFirst()
    {
        LearningCandidate olderOther = Candidate(LearningGround.Other, 1, 1);
        LearningCandidate newerOther = Candidate(LearningGround.Other, 9, 1);
        LearningCandidate answered = Candidate(LearningGround.Answered, 2, 1);
        LearningCandidate olderConfirmed = Candidate(LearningGround.Confirmed, 3, 1);
        LearningCandidate newerConfirmed = Candidate(LearningGround.Confirmed, 4, 1);

        IReadOnlyList<RecordingId> chosen = LearningSelection.Choose([olderOther, newerOther, answered, olderConfirmed, newerConfirmed]);

        Assert.Equal([newerConfirmed.Recording, olderConfirmed.Recording, answered.Recording, newerOther.Recording, olderOther.Recording], chosen);
    }

    [Fact(DisplayName = "BR-CD-004: a round reads no more than a hundred and twenty hours, passing over what does not fit for what still does")]
    public void ARoundReadsNoMoreThanAHundredAndTwentyHours()
    {
        LearningCandidate confirmed = Candidate(LearningGround.Confirmed, 5, 100);
        LearningCandidate answered = Candidate(LearningGround.Answered, 4, 15);
        LearningCandidate tooLong = Candidate(LearningGround.Other, 3, 6);
        LearningCandidate fits = Candidate(LearningGround.Other, 2, 5);
        LearningCandidate nothingLeft = Candidate(LearningGround.Other, 1, 1);
        LearningCandidate unread = new(RecordingId.New(), Monday, TimeSpan.Zero, LearningGround.Confirmed);

        IReadOnlyList<RecordingId> chosen = LearningSelection.Choose([nothingLeft, fits, tooLong, answered, confirmed, unread]);

        Assert.Equal([confirmed.Recording, answered.Recording, fits.Recording], chosen);
        Assert.Equal(TimeSpan.FromHours(120), LearningSelection.MostAtOnce);
    }

    private static LearningCandidate Candidate(LearningGround ground, int day, double hours)
        => new(RecordingId.New(), Monday.AddDays(day), TimeSpan.FromHours(hours), ground);
}
