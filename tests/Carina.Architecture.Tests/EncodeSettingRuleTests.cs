namespace Carina.Architecture.Tests;

public sealed class EncodeSettingRuleTests
{
    private const string Profile = "EncodeProfile";

    private const string Destination = "EncodeDestination";

    private static readonly string[] Kept =
    [
        "/Carina.Domain/Encodings/EncodeDestination.cs EncodeDestination.DefaultProfileId EncodeProfileId",
        "/Carina.Domain/Encodings/EncodeDestination.cs EncodeDestination.DefinedAt DateTime",
        "/Carina.Domain/Encodings/EncodeDestination.cs EncodeDestination.Id EncodeDestinationId",
        "/Carina.Domain/Encodings/EncodeDestination.cs EncodeDestination.Label EncodeLabel",
        "/Carina.Domain/Encodings/EncodeDestination.cs EncodeDestination.OutputRoot OutputRoot",
        "/Carina.Domain/Encodings/EncodeFailure.cs EncodeFailureDetail.Failure EncodeFailure",
        "/Carina.Domain/Encodings/EncodeFailure.cs EncodeFailureDetail.Note string",
        "/Carina.Domain/Encodings/EncodeFailure.cs EncodeFailureDetail.NoticedAt DateTime",
        "/Carina.Domain/Encodings/EncodeJob.cs EncodeJob.ArtefactName EncodeFileName?",
        "/Carina.Domain/Encodings/EncodeJob.cs EncodeJob.Attempt int",
        "/Carina.Domain/Encodings/EncodeJob.cs EncodeJob.DestinationId EncodeDestinationId",
        "/Carina.Domain/Encodings/EncodeJob.cs EncodeJob.EndedAt DateTime?",
        "/Carina.Domain/Encodings/EncodeJob.cs EncodeJob.Failure EncodeFailureDetail?",
        "/Carina.Domain/Encodings/EncodeJob.cs EncodeJob.Id EncodeJobId",
        "/Carina.Domain/Encodings/EncodeJob.cs EncodeJob.OutputRoot OutputRoot",
        "/Carina.Domain/Encodings/EncodeJob.cs EncodeJob.ProfileId EncodeProfileId",
        "/Carina.Domain/Encodings/EncodeJob.cs EncodeJob.QueuedAt DateTime",
        "/Carina.Domain/Encodings/EncodeJob.cs EncodeJob.RecordingId RecordingId",
        "/Carina.Domain/Encodings/EncodeJob.cs EncodeJob.StartedAt DateTime?",
        "/Carina.Domain/Encodings/EncodeJob.cs EncodeJob.Status EncodeJobStatus",
        "/Carina.Domain/Encodings/EncodePlan.cs EncodePlan.Encoder EncodeEncoder?",
        "/Carina.Domain/Encodings/EncodePlan.cs EncodePlan.Note string",
        "/Carina.Domain/Encodings/EncodePlan.cs EncodePlan.Refused EncodeFailure?",
        "/Carina.Domain/Encodings/EncodePlan.cs EncodePlan.Swerved EncodeSwerve?",
        "/Carina.Domain/Encodings/EncodeProfile.cs EncodeProfile.Codec EncodeCodec",
        "/Carina.Domain/Encodings/EncodeProfile.cs EncodeProfile.DefinedAt DateTime",
        "/Carina.Domain/Encodings/EncodeProfile.cs EncodeProfile.Deinterlace Deinterlace",
        "/Carina.Domain/Encodings/EncodeProfile.cs EncodeProfile.Id EncodeProfileId",
        "/Carina.Domain/Encodings/EncodeProfile.cs EncodeProfile.Label EncodeLabel",
        "/Carina.Domain/Encodings/EncodeProfile.cs EncodeProfile.Resolution EncodeResolution",
        "/Carina.Domain/Encodings/EncodeProfile.cs EncodeProfile.SoftwareRateControl ConstantRateFactor",
        "/Carina.Domain/Encodings/EncodeProfile.cs EncodeProfile.VaapiRateControl ConstantQuantiser",
        "/Carina.Domain/Encodings/EncodeProgress.cs EncodeProgress.Ended bool",
        "/Carina.Domain/Encodings/EncodeProgress.cs EncodeProgress.Reached TimeSpan",
        "/Carina.Domain/Encodings/EncodeProgress.cs EncodeProgress.Speed double",
        "/Carina.Domain/Encodings/EncodeProgress.cs EncodeProgress.Whole TimeSpan?",
        "/Carina.Domain/Encodings/EncodeRateControl.cs ConstantQuantiser.Quantiser int",
        "/Carina.Domain/Encodings/EncodeRateControl.cs ConstantRateFactor.RateFactor int",
        "/Carina.Domain/Encodings/EncodeScratchFile.cs EncodeScratchFile.Fate EncodeScratchFate?",
        "/Carina.Domain/Encodings/EncodeScratchFile.cs EncodeScratchFile.FileName EncodeFileName",
        "/Carina.Domain/Encodings/EncodeScratchFile.cs EncodeScratchFile.Id EncodeScratchFileId",
        "/Carina.Domain/Encodings/EncodeScratchFile.cs EncodeScratchFile.JobId EncodeJobId",
        "/Carina.Domain/Encodings/EncodeScratchFile.cs EncodeScratchFile.Kind EncodeScratchKind",
        "/Carina.Domain/Encodings/EncodeScratchFile.cs EncodeScratchFile.OutputRoot OutputRoot",
        "/Carina.Domain/Encodings/EncodeScratchFile.cs EncodeScratchFile.RemovedAt DateTime?",
        "/Carina.Domain/Encodings/EncodeScratchFile.cs EncodeScratchFile.WrittenAt DateTime",
        "/Carina.Domain/Encodings/EncodeSettings.cs EncodeSettings.BeforeFirstLook TimeSpan",
        "/Carina.Domain/Encodings/EncodeSettings.cs EncodeSettings.BetweenLooks TimeSpan",
        "/Carina.Domain/Encodings/EncodeSettings.cs EncodeSettings.MostAttempts int",
        "/Carina.Domain/Encodings/EncodeSettings.cs EncodeSettings.Prefer EncodeEncoder",
        "/Carina.Domain/Encodings/EncodeSettings.cs EncodeSettings.StalledAfter TimeSpan",
        "/Carina.Domain/Encodings/EncodeSettings.cs EncodeSettings.WorkedIn string?",
        "/Carina.Domain/Encodings/EncodeValidation.cs EncodeDestinationDraft.DefaultProfileId EncodeProfileId?",
        "/Carina.Domain/Encodings/EncodeValidation.cs EncodeDestinationDraft.Label string?",
        "/Carina.Domain/Encodings/EncodeValidation.cs EncodeDestinationDraft.OutputRoot string?",
        "/Carina.Domain/Encodings/EncodeValidation.cs EncodeProfileDraft.Codec EncodeCodec",
        "/Carina.Domain/Encodings/EncodeValidation.cs EncodeProfileDraft.Deinterlace Deinterlace",
        "/Carina.Domain/Encodings/EncodeValidation.cs EncodeProfileDraft.Label string?",
        "/Carina.Domain/Encodings/EncodeValidation.cs EncodeProfileDraft.Quantiser int",
        "/Carina.Domain/Encodings/EncodeValidation.cs EncodeProfileDraft.RateFactor int",
        "/Carina.Domain/Encodings/EncodeValidation.cs EncodeProfileDraft.Resolution EncodeResolution",
        "/Carina.Domain/Encodings/IEncodeJobRepository.cs EncodeClaim.Job EncodeJob?",
        "/Carina.Domain/Encodings/IEncodeJobRepository.cs EncodeClaim.Standing EncodeClaimStanding",
        "/Carina.Domain/Encodings/SourceLengthReading.cs SourceLengthReading.ExitCode int?",
        "/Carina.Domain/Encodings/SourceLengthReading.cs SourceLengthReading.Fault SourceLengthFault?",
        "/Carina.Domain/Encodings/SourceLengthReading.cs SourceLengthReading.Length TimeSpan?",
        "/Carina.Domain/Encodings/SourceLengthReading.cs SourceLengthReading.Note string",
    ];

    private static readonly string[] WorkedOut =
    [
        "/Carina.Domain/Encodings/EncodeDestinationId.cs EncodeDestinationId.Wire string",
        "/Carina.Domain/Encodings/EncodeJob.cs EncodeJob.HasEnded bool",
        "/Carina.Domain/Encodings/EncodeJob.cs EncodeJob.Standing EncodeStanding",
        "/Carina.Domain/Encodings/EncodeJob.cs EncodeJob.WorkFileName EncodeFileName",
        "/Carina.Domain/Encodings/EncodeJobId.cs EncodeJobId.Wire string",
        "/Carina.Domain/Encodings/EncodePlan.cs EncodePlan.CanRun bool",
        "/Carina.Domain/Encodings/EncodeProfileId.cs EncodeProfileId.Wire string",
        "/Carina.Domain/Encodings/EncodeProgress.cs EncodeProgress.Left TimeSpan?",
        "/Carina.Domain/Encodings/EncodeProgress.cs EncodeProgress.Portion double?",
        "/Carina.Domain/Encodings/EncodeScratchFile.cs EncodeScratchFile.IsOwedARemoval bool",
        "/Carina.Domain/Encodings/SourceLengthReading.cs SourceLengthReading.Measured bool",
    ];

    private static readonly string[] TheOnlyFreeTextTakenIn =
    [
        "/Carina.Domain/Encodings/EncodeFailure.cs EncodeFailureDetail.Note string",
        "/Carina.Domain/Encodings/EncodePlan.cs EncodePlan.Note string",
        "/Carina.Domain/Encodings/EncodeSettings.cs EncodeSettings.WorkedIn string?",
        "/Carina.Domain/Encodings/EncodeValidation.cs EncodeDestinationDraft.Label string?",
        "/Carina.Domain/Encodings/EncodeValidation.cs EncodeDestinationDraft.OutputRoot string?",
        "/Carina.Domain/Encodings/EncodeValidation.cs EncodeProfileDraft.Label string?",
        "/Carina.Domain/Encodings/SourceLengthReading.cs SourceLengthReading.Note string",
    ];

    private static string Settings => Path.Combine(RepositoryLayout.SourceDirectory, "Carina.Domain", "Encodings");

    private static IEnumerable<string> AsItStandsToday
        => EncodeSettingRules.WhatASettingKeeps(RepositoryLayout.SourceDirectory, Settings);

    [Fact(DisplayName = "BR-EV-001: everything a profile or a destination keeps is written down here")]
    public void EverythingTheEncodeDomainKeepsIsWrittenDownHere()
    {
        Assert.Equal(Kept, EncodeSettingRules.WhatASettingKeeps(RepositoryLayout.SourceDirectory, Settings));
    }

    [Fact(DisplayName = "BR-EV-001: everything the encode domain works out from what it keeps is written down here")]
    public void EverythingTheEncodeDomainWorksOutIsWrittenDownHere()
    {
        Assert.Equal(WorkedOut, EncodeSettingRules.WhatASettingWorksOut(RepositoryLayout.SourceDirectory, Settings));
    }

    [Fact(DisplayName = "BR-EV-001: neither a profile nor a destination keeps a piece of free text")]
    public void NeitherAProfileNorADestinationKeepsAPieceOfFreeText()
    {
        Assert.DoesNotContain(AsItStandsToday.Where(Settled), EncodeSettingRules.IsFreeText);
    }

    [Fact(DisplayName = "BR-EV-001: a profile and a destination are made of nothing but the kinds named here")]
    public void AProfileAndADestinationAreMadeOfNothingButTheKindsNamedHere()
    {
        Assert.Equal(
            [
                "ConstantQuantiser",
                "ConstantRateFactor",
                "DateTime",
                "Deinterlace",
                "EncodeCodec",
                "EncodeDestinationId",
                "EncodeLabel",
                "EncodeProfileId",
                "EncodeResolution",
                "OutputRoot",
            ],
            AsItStandsToday.Where(Settled).Select(EncodeSettingRules.Kind).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    [Fact(DisplayName = "BR-EV-001: the only free text the encode domain takes in at all is written down here")]
    public void TheOnlyFreeTextTheEncodeDomainTakesInAtAllIsWrittenDownHere()
    {
        Assert.Equal(TheOnlyFreeTextTakenIn, AsItStandsToday.Where(EncodeSettingRules.IsFreeText).ToArray());
    }

    [Fact(DisplayName = "BR-EV-001: the free text a draft carries is turned into something else before it is kept")]
    public void TheFreeTextADraftCarriesIsTurnedIntoSomethingElseBeforeItIsKept()
    {
        string[] typedIn =
        [
            .. TheOnlyFreeTextTakenIn
                .Where(entry => entry.Contains("Draft.", StringComparison.Ordinal))
                .Select(EncodeSettingRules.Named),
        ];

        Assert.Equal(["Label", "Label", "OutputRoot"], typedIn.Order(StringComparer.Ordinal));

        Assert.All(
            AsItStandsToday.Where(Settled).Where(kept => typedIn.Contains(EncodeSettingRules.Named(kept), StringComparer.Ordinal)),
            kept => Assert.False(EncodeSettingRules.IsFreeText(kept), kept));
    }

    [Fact(DisplayName = "BR-EV-001: nothing a person types reaches the place the command line is built")]
    public void NothingAPersonTypesReachesThePlaceTheCommandLineIsBuilt()
    {
        string builder = File.ReadAllText(Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Carina.Infrastructure",
            "Encodings",
            "FfmpegEncodeInvocation.cs"));

        Assert.DoesNotContain(".Label", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("Draft", builder, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-EV-001: the one piece of free text a setting keeps is where work files go, and no draft carries it")]
    public void TheOnePieceOfFreeTextASettingKeepsIsWhereWorkFilesGo()
    {
        Assert.Equal(
            ["/Carina.Domain/Encodings/EncodeSettings.cs EncodeSettings.WorkedIn string?"],
            Kept.Where(entry => entry.Contains(" EncodeSettings.", StringComparison.Ordinal)).Where(EncodeSettingRules.IsFreeText));

        Assert.DoesNotContain(TheOnlyFreeTextTakenIn, entry => entry.Contains("Draft.WorkedIn", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "BR-EV-004: nothing in the encode domain is named for a bitrate, so no card can be handed one")]
    public void NothingInTheEncodeDomainIsNamedForABitrate()
    {
        Assert.Empty(EncodeSettingRules.WhatIsNamedForABitrate(Settings));
    }

    private static bool Settled(string entry)
        => entry.Contains($" {Profile}.", StringComparison.Ordinal)
            || entry.Contains($" {Destination}.", StringComparison.Ordinal);
}
