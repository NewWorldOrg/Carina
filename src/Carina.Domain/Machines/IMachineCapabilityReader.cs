namespace Carina.Domain.Machines;

public interface IMachineCapabilityReader
{
    Task<MachineCapabilities> ReadAsync(CancellationToken cancellationToken);
}
