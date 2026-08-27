using Carina.Domain.Recordings;

namespace Carina.Api.Responder.Storage;

public sealed record StorageRootResponder(
    string Name,
    long FreeBytes,
    long TotalBytes,
    bool Writable,
    long CommittedBytes,
    int RecordingsInFlight,
    DiskShortfall? Shortfall)
{
    public static StorageRootResponder Of(StorageRootStanding standing)
    {
        ArgumentNullException.ThrowIfNull(standing);

        return new StorageRootResponder(
            standing.Name,
            standing.FreeBytes,
            standing.TotalBytes,
            standing.Writable,
            long.CreateSaturating(standing.CommittedBytes),
            standing.RecordingsInFlight,
            standing.Shortfall);
    }
}

public sealed record StorageResponder(IReadOnlyList<StorageRootResponder> Roots)
{
    public static StorageResponder Of(IReadOnlyList<StorageRootStanding> standing)
    {
        ArgumentNullException.ThrowIfNull(standing);

        return new StorageResponder([.. standing.Select(StorageRootResponder.Of)]);
    }
}
