using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Carina.Infrastructure.Tests;

public sealed class RecordingKeylessRuleTests
{
    private const string Ledger = "recording";

    private static readonly IReadOnlyList<string> WhatItMustNotPointAt =
    [
        "broadcast_service",
        "candidate_channel",
        "programme",
    ];

    private static CarinaDbContext Carina()
    {
        var builder = new DbContextOptionsBuilder<CarinaDbContext>();
        builder.UseCarinaDatabase("Host=db;Port=5432;Database=carina;Username=carina;Password=placeholder");

        return new CarinaDbContext(builder.Options);
    }

    private static IEntityType TableOf(CarinaDbContext context, string table)
        => context.Model.GetEntityTypes().Single(entityType => entityType.GetTableName() == table);

    [Fact(DisplayName = "BR-KD-013: the ledger declares no foreign key at all")]
    public void TheLedgerDeclaresNoForeignKeyAtAll()
    {
        using CarinaDbContext context = Carina();

        Assert.Empty(TableOf(context, Ledger)
            .GetForeignKeys()
            .Select(key => $"{Ledger} -> {key.PrincipalEntityType.GetTableName()}")
            .Order(StringComparer.Ordinal));
    }

    [Fact(DisplayName = "BR-KD-013: rebuilding the guide or dropping a channel definition drags no recording away")]
    public void RebuildingTheGuideOrDroppingAChannelDefinitionDragsNoRecordingAway()
    {
        using CarinaDbContext context = Carina();

        Assert.Empty(context.Model
            .GetEntityTypes()
            .SelectMany(entityType => entityType.GetForeignKeys())
            .Where(key => key.DeclaringEntityType.GetTableName() == Ledger
                || key.PrincipalEntityType.GetTableName() == Ledger)
            .Select(key => $"{key.DeclaringEntityType.GetTableName()} -> {key.PrincipalEntityType.GetTableName()}")
            .Order(StringComparer.Ordinal));
    }

    [Fact(DisplayName = "BR-KD-013: the three tables it must not point at are in the model, for the rule to have weighed them")]
    public void TheThreeTablesItMustNotPointAtAreInTheModel()
    {
        using CarinaDbContext context = Carina();

        IReadOnlyList<string> tables = [.. context.Model
            .GetEntityTypes()
            .Select(entityType => entityType.GetTableName() ?? string.Empty)];

        Assert.Contains(Ledger, tables, StringComparer.Ordinal);

        foreach (string table in WhatItMustNotPointAt)
        {
            Assert.Contains(table, tables, StringComparer.Ordinal);
        }
    }

    [Fact(DisplayName = "BR-KD-013: what the ledger keeps of the guide is a copy it took, not a key it holds")]
    public void WhatTheLedgerKeepsOfTheGuideIsACopyItTook()
    {
        using CarinaDbContext context = Carina();

        IReadOnlyList<string> columns = [.. TableOf(context, Ledger)
            .GetProperties()
            .Select(property => property.GetColumnName())];

        Assert.Contains("network_id", columns, StringComparer.Ordinal);
        Assert.Contains("service_id", columns, StringComparer.Ordinal);
        Assert.Contains("event_id", columns, StringComparer.Ordinal);
        Assert.Contains("programme_start_at", columns, StringComparer.Ordinal);
        Assert.Contains("snapshot_name", columns, StringComparer.Ordinal);
    }
}
