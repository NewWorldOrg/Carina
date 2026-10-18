using Carina.Domain.Segments;

namespace Carina.TestSupport;

/// <summary>
/// When what learning reads last changed, as a test sets it; none until it does.
/// </summary>
public sealed class HeldLearningMaterialChanges : ILearningMaterialChanges
{
    public DateTime? LastChanged { get; set; }

    public Task<DateTime?> LastChangedAsync(CancellationToken cancellationToken) => Task.FromResult(LastChanged);
}
