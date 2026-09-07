namespace Carina.Domain.Migration;

public sealed record SourceChannelDefinition
{
    public SourceChannelDefinition(long id, string name, SourceBroadcastKind kind, ServiceKey service, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(service);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "A channel of the source system is one it can name.");
        }

        Id = SourceRow.Of(id, nameof(id));
        Name = name;
        Kind = kind;
        Service = service;
        Enabled = enabled;
    }

    public long Id { get; }

    public string Name { get; }

    public SourceBroadcastKind Kind { get; }

    public ServiceKey Service { get; }

    public bool Enabled { get; }
}
