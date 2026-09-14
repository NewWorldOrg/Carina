using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Encodings;

public sealed class EncodeAutoRunReader(IEncodeAutoRunRepository rows, EncodeSettings deployed) : IEncodeAutoRunReader
{
    public async Task<EncodeAutoRunStanding> ReadAsync(CancellationToken cancellationToken)
        => EncodeAutoRunStanding.Over(await rows.ReadAsync(cancellationToken), deployed);
}
