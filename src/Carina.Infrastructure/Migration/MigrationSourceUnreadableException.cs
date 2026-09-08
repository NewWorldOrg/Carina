namespace Carina.Infrastructure.Migration;

public sealed class MigrationSourceUnreadableException(string message) : Exception(message)
{
}
