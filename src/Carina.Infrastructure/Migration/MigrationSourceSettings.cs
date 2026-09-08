using System.Diagnostics.CodeAnalysis;

using MySqlConnector;

namespace Carina.Infrastructure.Migration;

public sealed class MigrationSourceSettings
{
    public const string ConnectionVariable = "CARINA_MIGRATION_SOURCE_CONNECTION";

    private MigrationSourceSettings(string connection) => ConnectionAsSupplied = connection;

    public string ConnectionAsSupplied { get; }

    public static bool TryRead(
        Func<string, string?> environment,
        [NotNullWhen(true)] out MigrationSourceSettings? read,
        out string problem)
    {
        ArgumentNullException.ThrowIfNull(environment);

        read = null;
        string? given = environment(ConnectionVariable);

        if (string.IsNullOrWhiteSpace(given))
        {
            problem = $"Nothing says how to reach the system being replaced. Set {ConnectionVariable} to a "
                + "connection for an account that may only read from it, and run this again.";

            return false;
        }

        MySqlConnectionStringBuilder builder;

        try
        {
            builder = new MySqlConnectionStringBuilder(given);
        }
        catch (Exception unusable) when (unusable is ArgumentException or FormatException)
        {
            problem = $"{ConnectionVariable} does not read as a connection to the system being replaced.";

            return false;
        }

        if (string.IsNullOrWhiteSpace(builder.Server))
        {
            problem = $"{ConnectionVariable} names no host, and a run does not guess one.";

            return false;
        }

        if (string.IsNullOrWhiteSpace(builder.Database))
        {
            problem = $"{ConnectionVariable} names no database, and a run does not guess one.";

            return false;
        }

        if (string.IsNullOrWhiteSpace(builder.UserID))
        {
            problem = $"{ConnectionVariable} names no account, and a run does not guess one.";

            return false;
        }

        read = new MigrationSourceSettings(given);
        problem = string.Empty;

        return true;
    }

    public override string ToString() => $"whatever {ConnectionVariable} holds";
}
