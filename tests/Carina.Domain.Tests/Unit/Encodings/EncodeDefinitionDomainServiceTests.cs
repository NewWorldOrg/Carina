using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Unit.Encodings;

[Trait("Category", "Unit")]
public sealed class EncodeDefinitionDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime At = new(2026, 11, 12, 3, 0, 0, DateTimeKind.Utc);

    private readonly HeldEncodeJobs jobs = new();

    private readonly HeldEncodeDestinations destinations = new();

    private EncodeDefinitionDomainService Service => new(jobs, destinations);

    [Fact(DisplayName = "BR-ES-003: a profile or a destination a waiting or running job names answers that job, and one no such job names answers nothing")]
    public async Task ADefinitionAnUnfinishedJobNamesAnswersThatJob()
    {
        EncodeProfile profile = Profile();
        EncodeDestination destination = Destination(profile.Id);
        EncodeJob waiting = Job(profile.Id, destination.Id);
        jobs.Jobs.Add(waiting);

        Assert.Same(waiting, await Service.UnderwayWithAsync(profile.Id, Cancel));
        Assert.Same(waiting, await Service.UnderwayWithAsync(destination.Id, Cancel));
        Assert.Null(await Service.UnderwayWithAsync(EncodeProfileId.New(), Cancel));
        Assert.Null(await Service.UnderwayWithAsync(EncodeDestinationId.New(), Cancel));
    }

    [Fact(DisplayName = "BR-ED2-015: a profile no job ever named is deleted, and stays as it was")]
    public async Task AProfileNoJobNamedIsDeleted()
    {
        EncodeProfile profile = Profile();

        EncodeDefinitionRemoval removal = await Service.RemoveAsync(profile, At, Cancel);

        Assert.Equal(EncodeDefinitionFate.Deleted, removal.Fate);
        Assert.Null(removal.Refusal);
        Assert.False(profile.IsRetired);
    }

    [Fact(DisplayName = "BR-ED2-015: a profile a job that ended named is retired at the moment handed in")]
    public async Task AProfileAnEndedJobNamedIsRetired()
    {
        EncodeProfile profile = Profile();
        EncodeJob ended = Job(profile.Id, EncodeDestinationId.New());
        ended.Cancel(At.AddHours(-1));
        jobs.Jobs.Add(ended);

        EncodeDefinitionRemoval removal = await Service.RemoveAsync(profile, At, Cancel);

        Assert.Equal(EncodeDefinitionFate.Retired, removal.Fate);
        Assert.Equal(At, profile.RetiredAt);
    }

    [Fact(DisplayName = "BR-ED2-015: a retired profile is not removed again")]
    public async Task ARetiredProfileIsNotRemovedAgain()
    {
        EncodeProfile profile = Profile();
        profile.Retire(At.AddDays(-1));

        EncodeDefinitionRemoval removal = await Service.RemoveAsync(profile, At, Cancel);

        Assert.Null(removal.Fate);
        Assert.Equal(EncodeDefinitionRefusal.AlreadyRetired, removal.Refusal);
        Assert.Equal(At.AddDays(-1), profile.RetiredAt);
    }

    [Fact(DisplayName = "BR-ES-003: a profile a waiting job names is refused, naming the job, and is not retired")]
    public async Task AProfileAWaitingJobNamesIsRefused()
    {
        EncodeProfile profile = Profile();
        EncodeJob waiting = Job(profile.Id, EncodeDestinationId.New());
        jobs.Jobs.Add(waiting);

        EncodeDefinitionRemoval removal = await Service.RemoveAsync(profile, At, Cancel);

        Assert.Equal(EncodeDefinitionRefusal.StillInFlight, removal.Refusal);
        Assert.Same(waiting, removal.Underway);
        Assert.False(profile.IsRetired);
    }

    [Fact(DisplayName = "BR-ED2-016: a profile a destination still in use encodes with unless asked otherwise is refused, naming that destination")]
    public async Task AProfileADestinationNamesAsItsDefaultIsRefused()
    {
        EncodeProfile profile = Profile();
        EncodeDestination retired = Destination(profile.Id);
        retired.Retire(At.AddDays(-1));
        EncodeDestination naming = Destination(profile.Id);
        destinations.Destinations.AddRange([retired, naming]);

        EncodeDefinitionRemoval removal = await Service.RemoveAsync(profile, At, Cancel);

        Assert.Equal(EncodeDefinitionRefusal.StillTheDefault, removal.Refusal);
        Assert.Same(naming, removal.NamedBy);
        Assert.False(profile.IsRetired);
    }

    [Fact(DisplayName = "BR-ED2-016: a profile only a retired destination names as its default is removed")]
    public async Task AProfileOnlyARetiredDestinationNamesIsRemoved()
    {
        EncodeProfile profile = Profile();
        EncodeDestination retired = Destination(profile.Id);
        retired.Retire(At.AddDays(-1));
        destinations.Destinations.Add(retired);

        EncodeDefinitionRemoval removal = await Service.RemoveAsync(profile, At, Cancel);

        Assert.Equal(EncodeDefinitionFate.Deleted, removal.Fate);
    }

    [Fact(DisplayName = "BR-ED2-015: a destination no job ever named is deleted, beside another still in use")]
    public async Task ADestinationNoJobNamedIsDeleted()
    {
        EncodeDestination destination = Destination(EncodeProfileId.New());
        destinations.Destinations.AddRange([destination, Destination(EncodeProfileId.New())]);

        EncodeDefinitionRemoval removal = await Service.RemoveAsync(destination, At, Cancel);

        Assert.Equal(EncodeDefinitionFate.Deleted, removal.Fate);
        Assert.False(destination.IsRetired);
    }

    [Fact(DisplayName = "BR-ED2-015: a destination a job that ended named is retired at the moment handed in")]
    public async Task ADestinationAnEndedJobNamedIsRetired()
    {
        EncodeDestination destination = Destination(EncodeProfileId.New());
        destinations.Destinations.AddRange([destination, Destination(EncodeProfileId.New())]);
        EncodeJob ended = Job(EncodeProfileId.New(), destination.Id);
        ended.Cancel(At.AddHours(-1));
        jobs.Jobs.Add(ended);

        EncodeDefinitionRemoval removal = await Service.RemoveAsync(destination, At, Cancel);

        Assert.Equal(EncodeDefinitionFate.Retired, removal.Fate);
        Assert.Equal(At, destination.RetiredAt);
    }

    [Fact(DisplayName = "BR-ED2-015: a retired destination is not removed again")]
    public async Task ARetiredDestinationIsNotRemovedAgain()
    {
        EncodeDestination destination = Destination(EncodeProfileId.New());
        destination.Retire(At.AddDays(-1));
        destinations.Destinations.Add(destination);

        EncodeDefinitionRemoval removal = await Service.RemoveAsync(destination, At, Cancel);

        Assert.Equal(EncodeDefinitionRefusal.AlreadyRetired, removal.Refusal);
        Assert.Equal(At.AddDays(-1), destination.RetiredAt);
    }

    [Fact(DisplayName = "BR-ES-003: a destination a running job names is refused before it is weighed as the last one")]
    public async Task ADestinationARunningJobNamesIsRefused()
    {
        EncodeDestination destination = Destination(EncodeProfileId.New());
        destinations.Destinations.Add(destination);
        EncodeJob running = Job(EncodeProfileId.New(), destination.Id);
        running.Start(At.AddMinutes(-5));
        jobs.Jobs.Add(running);

        EncodeDefinitionRemoval removal = await Service.RemoveAsync(destination, At, Cancel);

        Assert.Equal(EncodeDefinitionRefusal.StillInFlight, removal.Refusal);
        Assert.Same(running, removal.Underway);
        Assert.False(destination.IsRetired);
    }

    [Fact(DisplayName = "BR-ED2-016: the one destination not retired is refused, whatever retired ones stand beside it")]
    public async Task TheLastDestinationIsRefused()
    {
        EncodeDestination retired = Destination(EncodeProfileId.New());
        retired.Retire(At.AddDays(-1));
        EncodeDestination last = Destination(EncodeProfileId.New());
        destinations.Destinations.AddRange([retired, last]);

        EncodeDefinitionRemoval removal = await Service.RemoveAsync(last, At, Cancel);

        Assert.Equal(EncodeDefinitionRefusal.TheLastOne, removal.Refusal);
        Assert.False(last.IsRetired);
    }

    private static EncodeProfile Profile()
        => EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Standard"),
            EncodeCodec.H264,
            EncodeResolution.AsSource,
            Deinterlace.Leave,
            new ConstantRateFactor(22),
            new ConstantQuantiser(24),
            At.AddDays(-10));

    private static EncodeDestination Destination(EncodeProfileId profile)
        => EncodeDestination.Define(
            EncodeDestinationId.New(),
            new EncodeLabel("Shelf"),
            new OutputRoot("shelf"),
            profile,
            At.AddDays(-10));

    private static EncodeJob Job(EncodeProfileId profile, EncodeDestinationId destination)
        => EncodeJob.Queue(EncodeJobId.New(), RecordingId.New(), profile, destination, new OutputRoot("shelf"), At.AddHours(-2));
}
