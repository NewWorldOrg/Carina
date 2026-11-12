using Carina.Domain.Channels;
using Carina.Domain.Migration;
using Carina.Domain.Reservations;

using static Carina.Domain.Tests.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Migration;

public sealed class MigrationRecordingArrivalTests
{
    [Fact(DisplayName = "BR-MS-001: a recording the source system says ended on the instant it began is not carried, as a shape this system cannot take")]
    public void ARecordingThatLastsNoTimeIsNotCarried()
    {
        MigrationVerdict judged = MigrationClassifier.OnARecording(
            new SourceRecording(7, "a programme", Began, Began, InReach, Programme),
            AsBroadcast(7, "one.m2ts", 100),
            OnDisk("one.m2ts", 90));

        Assert.False(judged.Carried);
        Assert.Equal(MigrationRefusal.Inexpressible, judged.Refusal);
        Assert.Equal("a programme", judged.Note);
        Assert.Equal(100, judged.Claimed);
        Assert.Equal(90, judged.Observed);
    }

    [Fact(DisplayName = "BR-MS-001: a recording that lasts a single tick is carried")]
    public void ARecordingThatLastsATickIsCarried()
    {
        MigrationVerdict judged = MigrationClassifier.OnARecording(
            new SourceRecording(7, "a programme", Began, Began.AddTicks(1), InReach, Programme),
            AsBroadcast(7, "one.m2ts", 100),
            OnDisk("one.m2ts", 100));

        Assert.True(judged.Carried);
    }

    [Fact(DisplayName = "BR-MS-001: a recording that names no programme and lasts no time is not carried because nothing identifies it")]
    public void WhatIdentifiesARecordingIsAskedBeforeHowLongItLasts()
    {
        MigrationVerdict judged = MigrationClassifier.OnARecording(
            new SourceRecording(7, "a programme", Began, Began, InReach, null),
            AsBroadcast(7, "one.m2ts", 100),
            OnDisk("one.m2ts", 100));

        Assert.Equal(MigrationRefusal.Unidentifiable, judged.Refusal);
    }

    [Fact(DisplayName = "BR-MS-001: a recording that lasts no time and has no file on the disk is refused for the missing file, which is what stops it first")]
    public void AMissingFileIsAskedBeforeHowLongTheRecordingLasts()
    {
        MigrationVerdict judged = MigrationClassifier.OnARecording(
            new SourceRecording(7, "a programme", Began, Began, InReach, Programme),
            AsBroadcast(7, "one.m2ts", 100),
            null);

        Assert.Equal(MigrationRefusal.FileMissing, judged.Refusal);
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

    [Fact(DisplayName = "BR-MD-001: the file of a recording that is carried is the one kept as broadcast, and an encoded copy beside it is not")]
    public void TheFileCarriedIsTheOneKeptAsBroadcast()
    {
        IReadOnlyDictionary<long, SourceRecordingFile> carried = MigrationClassifier.AsBroadcastByRecording(
            [Encoded(7, "one.mp4", 50), AsBroadcast(7, "one.m2ts", 100), Encoded(8, "two.mp4", 50)]);

        Assert.Single(carried);
        Assert.Equal("one.m2ts", carried[7].Path);
    }

    [Fact(DisplayName = "BR-MD-001: a recording the source system names two files as broadcast for is refused rather than carried twice")]
    public void TwoFilesAsBroadcastForOneRecordingAreRefused()
        => Assert.Throws<ArgumentException>(() => MigrationClassifier.AsBroadcastByRecording(
            [AsBroadcast(7, "one.m2ts", 100), AsBroadcast(7, "again.m2ts", 100)]));
}
