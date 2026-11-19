using Carina.Domain.Segments;

namespace Carina.TestSupport;

public sealed class HeldLearningMaterialChanges : ILearningMaterialChanges
{
    public DateTime? LastChanged { get; set; }

    public Task<DateTime?> LastChangedAsync(CancellationToken cancellationToken) => Task.FromResult(LastChanged);
}
