using Carina.Domain.Channels;
using Carina.Domain.Migration;
using Carina.Domain.Reservations;

using static Carina.Domain.Tests.Unit.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Unit.Migration;

[Trait("Category", "Unit")]
public sealed class SourceRecordingTests
{
    [Fact]
    public void ARecordingEndsAfterItStarts()
    {
        Assert.Throws<ArgumentException>(() => new SourceRecording(7, "a programme", Ended, Began, InReach, Programme));
    }

    [Fact(DisplayName = "a recording that names its service and programme names the programme as the guide here does, from the moment it began")]
    public void ARecordingThatNamesItsServiceAndProgrammeNamesTheProgramme()
    {
        ProgrammeRef? programme = Recording(7).Programmed;

        Assert.Equal(
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), Programme, Began),
            programme);
    }

    [Fact(DisplayName = "a recording that names no service or no programme names no programme")]
    public void ARecordingThatNamesNoServiceOrNoProgrammeNamesNone()
    {
        Assert.Null(Recording(7, null).Programmed);
        Assert.Null(RecordingOfNoProgramme(7).Programmed);
    }
}
