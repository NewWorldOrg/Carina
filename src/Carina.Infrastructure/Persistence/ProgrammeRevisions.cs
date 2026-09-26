namespace Carina.Infrastructure.Persistence;

public static class ProgrammeRevisions
{
    public const string Sequence = "programme_revision";

    public const long WriterLock = 5_243_197_610_101;

    /// <summary>
    /// Waits for the lock every programme write takes before it is handed revisions, and holds
    /// it until the transaction ends.
    /// </summary>
    public static readonly string TakeTurnSql = $"SELECT pg_advisory_xact_lock({WriterLock})";
}
