using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

using Carina.Infrastructure.Migration;

namespace Carina.Infrastructure.Tests.Migration;

internal sealed class StandInSourceDatabase : IMigrationSourceConnection
{
    private readonly Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> answers =
        new(StringComparer.Ordinal);

    public List<string> Statements { get; } = [];

    public int Opened { get; private set; }

    public int Closed { get; private set; }

    public Exception? WhenOpening { get; set; }

    public void Holds(string table, params IReadOnlyDictionary<string, object?>[] rows) => answers[table] = rows;

    public Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        if (WhenOpening is { } refused)
        {
            throw refused;
        }

        Opened++;

        return Task.FromResult<DbConnection>(new StandInConnection(this));
    }

    internal IReadOnlyList<IReadOnlyDictionary<string, object?>> RowsFor(string statement)
    {
        Statements.Add(statement);

        foreach (KeyValuePair<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> answer in answers)
        {
            if (statement.Contains($"FROM {answer.Key}", StringComparison.Ordinal))
            {
                return answer.Value;
            }
        }

        return [];
    }

    internal void Went() => Closed++;
}

internal sealed class StandInConnection(StandInSourceDatabase database) : DbConnection
{
    private ConnectionState state = ConnectionState.Open;

    [AllowNull]
    public override string ConnectionString { get; set; } = string.Empty;

    public override string Database => string.Empty;

    public override string DataSource => string.Empty;

    public override string ServerVersion => string.Empty;

    public override ConnectionState State => state;

    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

    public override void Close()
    {
        if (state is ConnectionState.Open)
        {
            database.Went();
        }

        state = ConnectionState.Closed;
    }

    public override void Open() => state = ConnectionState.Open;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }

        base.Dispose(disposing);
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => throw new NotSupportedException();

    protected override DbCommand CreateDbCommand() => new StandInCommand(database) { Connection = this };
}

internal sealed class StandInCommand(StandInSourceDatabase database) : DbCommand
{
    [AllowNull]
    public override string CommandText { get; set; } = string.Empty;

    public override int CommandTimeout { get; set; }

    public override CommandType CommandType { get; set; } = CommandType.Text;

    public override bool DesignTimeVisible { get; set; }

    public override UpdateRowSource UpdatedRowSource { get; set; }

    protected override DbConnection? DbConnection { get; set; }

    protected override DbParameterCollection DbParameterCollection => null!;

    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel()
    {
    }

    public override int ExecuteNonQuery()
    {
        database.RowsFor(CommandText);

        return 0;
    }

    public override object? ExecuteScalar() => throw new NotSupportedException();

    public override void Prepare() => throw new NotSupportedException();

    protected override DbParameter CreateDbParameter() => throw new NotSupportedException();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        => new StandInDataReader(database.RowsFor(CommandText));
}

internal sealed class StandInDataReader(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) : DbDataReader
{
    private int at = -1;

    private IReadOnlyList<string> Names => at < 0 || at >= rows.Count ? [] : [.. rows[at].Keys];

    public override int Depth => 0;

    public override int FieldCount => Names.Count;

    public override bool HasRows => rows.Count > 0;

    public override bool IsClosed => at >= rows.Count;

    public override int RecordsAffected => 0;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool GetBoolean(int ordinal) => Convert.ToBoolean(GetValue(ordinal));

    public override byte GetByte(int ordinal) => Convert.ToByte(GetValue(ordinal));

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        => throw new NotSupportedException();

    public override char GetChar(int ordinal) => throw new NotSupportedException();

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        => throw new NotSupportedException();

    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

    public override DateTime GetDateTime(int ordinal) => Convert.ToDateTime(GetValue(ordinal));

    public override decimal GetDecimal(int ordinal) => Convert.ToDecimal(GetValue(ordinal));

    public override double GetDouble(int ordinal) => Convert.ToDouble(GetValue(ordinal));

    public override IEnumerator GetEnumerator() => throw new NotSupportedException();

    public override Type GetFieldType(int ordinal) => GetValue(ordinal)?.GetType() ?? typeof(object);

    public override float GetFloat(int ordinal) => Convert.ToSingle(GetValue(ordinal));

    public override Guid GetGuid(int ordinal) => throw new NotSupportedException();

    public override short GetInt16(int ordinal) => Convert.ToInt16(GetValue(ordinal));

    public override int GetInt32(int ordinal) => Convert.ToInt32(GetValue(ordinal));

    public override long GetInt64(int ordinal) => Convert.ToInt64(GetValue(ordinal));

    public override string GetName(int ordinal) => Names[ordinal];

    public override int GetOrdinal(string name)
    {
        int found = Names.ToList().IndexOf(name);

        return found >= 0
            ? found
            : throw new IndexOutOfRangeException($"The stand-in source holds no column called '{name}'.");
    }

    public override string GetString(int ordinal) => (string)GetValue(ordinal);

    public override object GetValue(int ordinal) => rows[at][Names[ordinal]] ?? DBNull.Value;

    public override int GetValues(object[] values) => throw new NotSupportedException();

    public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;

    public override bool NextResult() => false;

    public override bool Read() => ++at < rows.Count;
}
