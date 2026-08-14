namespace Carina.Infrastructure.Persistence.Configurations;

internal static class PersistenceChecks
{
    public const string ReachableTuning = """
        (tune_system = 'IsdbT' AND physical_channel BETWEEN 13 AND 62 AND transport_stream_id IS NULL)
        OR (tune_system = 'IsdbSBs' AND physical_channel BETWEEN 1 AND 23 AND physical_channel % 2 = 1
            AND physical_channel NOT IN (7, 17) AND transport_stream_id IS NOT NULL)
        OR (tune_system = 'IsdbSCs110' AND physical_channel BETWEEN 2 AND 24 AND physical_channel % 2 = 0
            AND transport_stream_id IS NULL)
        """;

    public static string QualityOnlyWhenLocked(string measuredAt, string locked, string cnr)
        => $"{measuredAt} IS NULL OR {locked} OR {cnr} IS NULL";
}
