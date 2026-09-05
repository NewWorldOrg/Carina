namespace Carina.Architecture.Tests;

public sealed class QualityRuleSelfCheckTests
{
    private const string InTheFeature = "Carina.Domain/Quality/QualityAggregator.cs";

    private const string LayingOutATable = "Carina.Infrastructure/Persistence/Configurations/QualityIncidentConfiguration.cs";

    public static TheoryData<string, string> EveryOrdinaryWayOfDeclaringAForeignKey => new()
    {
        { """builder.HasOne<Recording>().WithMany().HasForeignKey(incident => incident.RecordingId);""", "HasForeignKey" },
        { """builder.HasMany<Recording>();""", "HasMany" },
        { """table.Sql("ALTER TABLE quality_incidents ADD FOREIGN KEY (recording_id) REFERENCES recording (id)");""", "FOREIGNKEY" },
    };

    public static TheoryData<string, string> EveryOrdinaryWayOfWritingAnotherDomainsLedger => new()
    {
        { """public QualityRound(IRecordingRepository recordings) { }""", "IRecordingRepository" },
        { """await programmes.AbsorbAsync(visit, token);""", ".AbsorbAsync(" },
        { """public QualityRound(CarinaDbContext context) { }""", "CarinaDbContext" },
        { """await context.Set<Recording>().ExecuteUpdateAsync(setter, token);""", "ExecuteUpdateAsync" },
        { """const string Sql = "UPDATE recording SET cc_measured = true";""", "UPDATErecordingSET" },
        { """const string Sql = "DELETE FROM reservation";""", "DELETEFROM" },
    };

    public static TheoryData<string, string> EveryOrdinaryWayOfOfferingADeletion => new()
    {
        { """[HttpDelete]""", "HttpDelete" },
        { """app.MapDelete("/api/quality/incidents/{id}", Invoke);""", "MapDelete" },
        { """[EndpointEffect(EndpointEffect.Destructive)]""", "EndpointEffect.Destructive" },
    };

    [Theory]
    [MemberData(nameof(EveryOrdinaryWayOfDeclaringAForeignKey))]
    public void DetectsThisWayOfDeclaringAForeignKey(string source, string reported)
    {
        using var tree = new SourceTree();
        tree.Write(LayingOutATable, source);

        Assert.Contains($"/{LayingOutATable} {reported}", QualityRules.WhatDeclaresAForeignKey(tree.Root));
    }

    [Fact]
    public void ReadsOnlyTheFilesThatLayOutTheQualityTablesWhenItLooksForAForeignKey()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/Persistence/Configurations/RecordingConfiguration.cs",
            """builder.HasOne<Reservation>().WithMany().HasForeignKey(recording => recording.ReservationId);""");

        Assert.Empty(QualityRules.WhatDeclaresAForeignKey(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryOrdinaryWayOfWritingAnotherDomainsLedger))]
    public void DetectsThisWayOfWritingAnotherDomainsLedger(string source, string reported)
    {
        using var tree = new SourceTree();
        tree.Write(InTheFeature, source);

        IReadOnlyList<string> found = QualityRules.WhatWritesAnotherDomainsLedger(tree.Root);

        Assert.Contains($"/{InTheFeature} {reported}", found);
    }

    [Fact(DisplayName = "BR-QD-012: the candidate channel score is the one thing quality writes outside itself")]
    public void TheCandidateChannelScoreIsTheOneThingQualityWritesOutsideItself()
    {
        using var tree = new SourceTree();
        tree.Write(
            InTheFeature,
            string.Join(
                Environment.NewLine,
                QualityRules.TheOneLedgerQualityMayWriteTo.Select(name => $"public QualityRound({name} candidates) {{ }}")));

        Assert.Empty(QualityRules.WhatWritesAnotherDomainsLedger(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryOrdinaryWayOfOfferingADeletion))]
    public void DetectsThisWayOfOfferingADeletion(string source, string reported)
    {
        using var tree = new SourceTree();
        tree.Write(InTheFeature, source);

        Assert.Equal([$"/{InTheFeature} {reported}"], QualityRules.WhatOffersAWayToDeleteSomething(tree.Root));
    }

    [Fact(DisplayName = "BR-QA-001: sweeping raw samples off by their age is a batch, not an endpoint")]
    public void SweepingRawSamplesOffByTheirAgeIsABatchNotAnEndpoint()
    {
        using var tree = new SourceTree();
        tree.Write(InTheFeature, """Task<int> ForgetTakenBeforeAsync(DateTime cutoff, CancellationToken cancellationToken);""");

        Assert.Empty(QualityRules.WhatOffersAWayToDeleteSomething(tree.Root));
    }

    [Theory]
    [InlineData("""if (RecordingQuality.Of(counters, scrambled) is QualityLevel.Warning) { }""", "QualityLevel")]
    [InlineData("""incident.Classification = TuneFailureKind.NoLock.ToString();""", "TuneFailureKind")]
    [InlineData("""if (visit.Outcome is VisitOutcome.Incomplete) { }""", "VisitOutcome")]
    [InlineData("""if (reach is ServiceReach.None) { }""", "ServiceReach")]
    public void DetectsThisWayOfDecidingAnAnomalyItDoesNotOwn(string source, string reported)
    {
        using var tree = new SourceTree();
        tree.Write(InTheFeature, source);

        Assert.Contains($"/{InTheFeature} {reported}", QualityRules.WhatDecidesAnAnomalyItDoesNotOwn(tree.Root));
    }

    [Fact(DisplayName = "BR-QD-002: an anomaly kept as the owner's own code walks straight past these marks")]
    public void AnAnomalyKeptAsTheOwnersOwnCodeWalksStraightPastTheseMarks()
    {
        using var tree = new SourceTree();
        tree.Write(InTheFeature, """incident.Classification = whatTheTunerSaid;""");

        Assert.Empty(QualityRules.WhatDecidesAnAnomalyItDoesNotOwn(tree.Root));
    }

    [Fact]
    public void ReadsTheApiHalfOfTheFeatureAsWellAsTheDomainHalf()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Api/Controllers/Incidents/ForgetIncidentAction.cs",
            """
            namespace Carina.Api.Controllers.Quality;

            [HttpDelete]
            public sealed class ForgetIncidentAction;
            """);

        Assert.Equal(
            ["/Carina.Api/Controllers/Incidents/ForgetIncidentAction.cs HttpDelete"],
            QualityRules.WhatOffersAWayToDeleteSomething(tree.Root));
    }

    [Fact]
    public void LeavesTheCompositionRootAlone()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Api/Program.cs",
            """
            using Carina.Api.Controllers.Quality;

            app.MapDelete("/api/recordings/{id}", Invoke);
            """);

        Assert.Empty(QualityRules.WhatOffersAWayToDeleteSomething(tree.Root));
    }

    [Fact]
    public void ReadsNothingOutOfBuildOutput()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Domain/obj/Quality/Generated.cs", """[HttpDelete]""");

        Assert.Empty(QualityRules.WhatOffersAWayToDeleteSomething(tree.Root));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-quality-rules-");

        public string Root => directory.FullName;

        public void Write(string path, string source)
        {
            string full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, source);
        }

        public void Dispose() => directory.Delete(recursive: true);
    }
}
