namespace Carina.Domain.Migration;

public sealed record SourceChannelDefinition
{
    public const int PhysicalChannelMaxLength = 64;

    public SourceChannelDefinition(
        long id,
        string name,
        SourceBroadcastKind kind,
        ServiceKey service,
        string physicalChannel)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(physicalChannel);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "A channel of the source system is one it can name.");
        }

        if (physicalChannel.Length > PhysicalChannelMaxLength)
        {
            throw new ArgumentException(
                $"A physical channel is at most {PhysicalChannelMaxLength} characters, "
                + $"but this one has {physicalChannel.Length}.",
                nameof(physicalChannel));
        }

        Id = SourceRow.Of(id, nameof(id));
        Name = name;
        Kind = kind;
        Service = service;
        PhysicalChannel = physicalChannel;
    }

    public long Id { get; }

    public string Name { get; }

    public SourceBroadcastKind Kind { get; }

    public ServiceKey Service { get; }

    public string PhysicalChannel { get; }
}
