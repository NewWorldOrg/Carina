namespace Carina.Domain.Integrity;

public enum IntegrityFault
{
    SizeDisagrees = 1,

    NoLedgerRow = 2,

    FileMissing = 3,

    FileEmpty = 4,
}
