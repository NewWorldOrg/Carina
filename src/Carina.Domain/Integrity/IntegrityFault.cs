namespace Carina.Domain.Integrity;

public enum IntegrityFault
{
    SizeDisagrees = 1,

    NoLedgerRow = 2,

    FileMissing = 3,

    FileEmpty = 4,

    EmptyThoughComplete = 5,
}

public static class IntegrityFaults
{
    public static readonly IReadOnlyList<IntegrityFault> ThatNameARecording =
    [
        IntegrityFault.SizeDisagrees,
        IntegrityFault.FileMissing,
        IntegrityFault.FileEmpty,
        IntegrityFault.EmptyThoughComplete,
    ];

    public static readonly IReadOnlyList<IntegrityFault> ThatWeighedTheFile =
    [
        IntegrityFault.SizeDisagrees,
        IntegrityFault.NoLedgerRow,
        IntegrityFault.FileEmpty,
        IntegrityFault.EmptyThoughComplete,
    ];

    public static IntegrityFault Named(IntegrityFault fault)
        => Enum.IsDefined(fault)
            ? fault
            : throw new ArgumentOutOfRangeException(nameof(fault), fault, "A finding is one the sweep can class.");
}
