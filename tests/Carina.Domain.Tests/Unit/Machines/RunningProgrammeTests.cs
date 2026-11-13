using Carina.Domain.Machines;

namespace Carina.Domain.Tests.Unit.Machines;

[Trait("Category", "Unit")]
public sealed class RunningProgrammeTests
{
    private static readonly DateTime Began = new(2026, 9, 5, 3, 0, 5, DateTimeKind.Utc);

    [Fact(DisplayName = "a programme is its id and when it began together; an id below the first or a time not in UTC is no programme")]
    public void AProgrammeIsItsIdAndWhenItBeganTogether()
    {
        var programme = new RunningProgramme(4242, Began);

        Assert.Equal(4242, programme.ProcessId);
        Assert.Equal(Began, programme.StartedAt);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RunningProgramme(0, Began));
        Assert.Throws<ArgumentException>(() => new RunningProgramme(4242, DateTime.SpecifyKind(Began, DateTimeKind.Local)));
    }

    [Fact(DisplayName = "what runs under the id now is the written programme when it began within the tolerance of when the written one did, on either side, and not otherwise")]
    public void WhatRunsUnderTheIdIsTheSameWhenItBeganWithinTheTolerance()
    {
        var programme = new RunningProgramme(4242, Began);
        TimeSpan tolerance = TimeSpan.FromSeconds(2);

        Assert.True(programme.IsTheSameAs(Began, tolerance));
        Assert.True(programme.IsTheSameAs(Began.AddSeconds(1.9), tolerance));
        Assert.True(programme.IsTheSameAs(Began.AddSeconds(-1.9), tolerance));
        Assert.False(programme.IsTheSameAs(Began.AddSeconds(2.1), tolerance));
        Assert.False(programme.IsTheSameAs(Began.AddHours(-1), tolerance));
        Assert.Throws<ArgumentOutOfRangeException>(() => programme.IsTheSameAs(Began, TimeSpan.FromSeconds(-1)));
    }

    [Fact(DisplayName = "held to a moment, a programme that began before it is written as begun at that moment under the same id, and one that began at or after it is written as it is")]
    public void HeldToAMomentAProgrammeThatBeganBeforeItIsWrittenAsBegunAtThatMoment()
    {
        var programme = new RunningProgramme(4242, Began);

        RunningProgramme held = programme.NoEarlierThan(Began.AddMilliseconds(400));
        RunningProgramme asIs = programme.NoEarlierThan(Began.AddMilliseconds(-400));

        Assert.Equal(4242, held.ProcessId);
        Assert.Equal(Began.AddMilliseconds(400), held.StartedAt);
        Assert.Same(programme, programme.NoEarlierThan(Began));
        Assert.Same(programme, asIs);
        Assert.Throws<ArgumentException>(() => programme.NoEarlierThan(DateTime.SpecifyKind(Began, DateTimeKind.Local)));
    }
}
