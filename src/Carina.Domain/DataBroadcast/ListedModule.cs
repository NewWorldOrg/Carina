namespace Carina.Domain.DataBroadcast;

public sealed record ListedModule
{
    public ListedModule(int id, int version, long size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);

        Id = CarouselNumbers.ModuleId(id, nameof(id));
        Version = CarouselNumbers.Version(version, nameof(version));
        Size = size;
    }

    public int Id { get; }

    public int Version { get; }

    public long Size { get; }
}
