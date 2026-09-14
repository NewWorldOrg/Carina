using Carina.Domain.Encodings;
using Carina.Domain.Machines;

namespace Carina.Infrastructure.Encodings;

public sealed class EncodeAutoRunReader(
    IEncodeAutoRunRepository rows,
    EncodeSettings deployed,
    MachineSettings machine) : IEncodeAutoRunReader
{
    public async Task<EncodeAutoRunStanding> ReadAsync(CancellationToken cancellationToken)
        => EncodeAutoRunStanding.Over(await rows.ReadAsync(cancellationToken), deployed, machine);
}
