namespace Carina.Domain.Migration;

public sealed class MigrationUnclassifiedException : Exception
{
    public MigrationUnclassifiedException()
    {
    }

    public MigrationUnclassifiedException(string message)
        : base(message)
    {
    }

    public MigrationUnclassifiedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
