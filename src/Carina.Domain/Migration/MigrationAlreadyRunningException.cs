namespace Carina.Domain.Migration;

public sealed class MigrationAlreadyRunningException : Exception
{
    public MigrationAlreadyRunningException()
    {
    }

    public MigrationAlreadyRunningException(string message)
        : base(message)
    {
    }

    public MigrationAlreadyRunningException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
