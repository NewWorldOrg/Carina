namespace Carina.TestSupport;

public sealed class ByTheOrderTheDatabaseReadsThem : IComparer<Guid>
{
    public static readonly ByTheOrderTheDatabaseReadsThem Comparer = new();

    private ByTheOrderTheDatabaseReadsThem()
    {
    }

    public int Compare(Guid left, Guid right)
        => ((ReadOnlySpan<byte>)left.ToByteArray(bigEndian: true))
            .SequenceCompareTo(right.ToByteArray(bigEndian: true));
}
