namespace Carina.Domain.Migration;

public interface IMigrationSourceDirectory
{
    Task<IReadOnlyList<SourceFile>> ListAsync(CancellationToken cancellationToken);
}
