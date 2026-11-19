namespace Carina.Domain.Segments;

public interface ILearningMaterialChanges
{
    Task<DateTime?> LastChangedAsync(CancellationToken cancellationToken);
}
