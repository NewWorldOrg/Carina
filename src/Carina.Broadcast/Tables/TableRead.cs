namespace Carina.Broadcast.Tables;

public abstract record TableRead<TTable>
    where TTable : class
{
    private TableRead()
    {
    }

    public sealed record Parsed : TableRead<TTable>
    {
        internal Parsed(TTable table)
        {
            Table = table;
        }

        public TTable Table { get; }
    }

    public sealed record Rejected : TableRead<TTable>
    {
        internal Rejected(TableDefect defect)
        {
            Defect = defect;
        }

        public TableDefect Defect { get; }
    }
}
