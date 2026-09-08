namespace Carina.Domain.Migration;

public sealed class MigrationCarryRefusedException : Exception
{
    public MigrationCarryRefusedException()
    {
    }

    public MigrationCarryRefusedException(string message)
        : base(message)
    {
    }

    public MigrationCarryRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
