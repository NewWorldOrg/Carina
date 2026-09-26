namespace Carina.Infrastructure.Persistence;

public static class ProgrammeRevisions
{
    public const string Sequence = "programme_revision";

    public const long WriterLock = 5_243_197_610_101;

    public static readonly string TakeTurnSql = $"SELECT pg_advisory_xact_lock({WriterLock})";
}
