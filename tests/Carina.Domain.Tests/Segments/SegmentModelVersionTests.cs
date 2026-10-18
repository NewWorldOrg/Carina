using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentModelVersionTests
{
    private static readonly DateTime Noon = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-CS-003: a version in use carries its parameters, the two kinds they tell apart, and when it was made and put in use")]
    public void AVersionInUseCarriesItsParameters()
    {
        SegmentModelVersion version = Version(1, SegmentModelVersionState.Active, adoptedAt: Noon);

        Assert.Equal(1, version.Number);
        Assert.Equal(SegmentModelVersionState.Active, version.State);
        Assert.False(version.ByHand);
        Assert.Equal(2, version.Kinds);
        Assert.Equal(SegmentModel.First, version.Model);
        Assert.Equal(Noon.AddHours(-1), version.TrainedAt);
        Assert.Equal(Noon, version.AdoptedAt);
        Assert.Null(version.RetiredAt);
    }

    [Fact(DisplayName = "BR-CS-003: only a candidate has never been put in use, and only a version used before has been taken out of use")]
    public void TheStateDecidesWhichTimesAreKnown()
    {
        Assert.Null(Version(2, SegmentModelVersionState.Candidate).AdoptedAt);
        Assert.Throws<ArgumentException>(() => Version(2, SegmentModelVersionState.Candidate, adoptedAt: Noon));
        Assert.Throws<ArgumentException>(() => Version(2, SegmentModelVersionState.Active));
        Assert.Throws<ArgumentException>(() => Version(2, SegmentModelVersionState.Retired, adoptedAt: Noon));
        Assert.Throws<ArgumentException>(() => Version(2, SegmentModelVersionState.Active, adoptedAt: Noon, retiredAt: Noon));
        Assert.Equal(Noon.AddHours(1), Version(2, SegmentModelVersionState.Retired, adoptedAt: Noon, retiredAt: Noon.AddHours(1)).RetiredAt);
    }

    [Fact(DisplayName = "BR-CS-003: only the version in use can be chosen by hand, and one chosen by hand says when")]
    public void OnlyTheVersionInUseCanBeChosenByHand()
    {
        SegmentModelVersion chosen = Version(3, SegmentModelVersionState.Active, adoptedAt: Noon, byHand: true, chosenAt: Noon);

        Assert.True(chosen.ByHand);
        Assert.Equal(Noon, chosen.ChosenByHandAt);
        Assert.Throws<ArgumentException>(() => Version(3, SegmentModelVersionState.Active, adoptedAt: Noon, byHand: true));
        Assert.Throws<ArgumentException>(
            () => Version(3, SegmentModelVersionState.Retired, adoptedAt: Noon, retiredAt: Noon, byHand: true, chosenAt: Noon));
        Assert.Throws<ArgumentException>(() => Version(3, SegmentModelVersionState.Candidate, byHand: true, chosenAt: Noon));
    }

    [Fact(DisplayName = "BR-CS-003: only a version taken out of use can have been rolled back from")]
    public void OnlyARetiredVersionCanHaveBeenRolledBackFrom()
    {
        Assert.Equal(
            Noon.AddDays(7),
            Version(4, SegmentModelVersionState.Retired, adoptedAt: Noon, retiredAt: Noon.AddDays(7), rolledBackAt: Noon.AddDays(7)).RolledBackAt);
        Assert.Throws<ArgumentException>(() => Version(4, SegmentModelVersionState.Active, adoptedAt: Noon, rolledBackAt: Noon.AddDays(7)));
    }

    [Fact(DisplayName = "versions are numbered from 1, in one of the three states, and every time is in UTC")]
    public void VersionsAreNumberedFromOneInUtc()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Version(0, SegmentModelVersionState.Active, adoptedAt: Noon));
        Assert.Throws<ArgumentOutOfRangeException>(() => Version(1, (SegmentModelVersionState)9, adoptedAt: Noon));
        Assert.Throws<ArgumentException>(() => Version(1, SegmentModelVersionState.Active, adoptedAt: DateTime.SpecifyKind(Noon, DateTimeKind.Local)));
        Assert.Throws<ArgumentException>(() => Version(1, SegmentModelVersionState.Active, adoptedAt: Noon.AddHours(-2)));
    }

    private static SegmentModelVersion Version(
        int number,
        SegmentModelVersionState state,
        DateTime? adoptedAt = null,
        DateTime? retiredAt = null,
        bool byHand = false,
        DateTime? chosenAt = null,
        DateTime? rolledBackAt = null)
        => SegmentModelVersion.Rehydrate(number, state, byHand, SegmentModel.First, Noon.AddHours(-1), adoptedAt, retiredAt, chosenAt, rolledBackAt);
}
