using Carina.Domain.Base;
using Carina.Domain.Programmes;

namespace Carina.Domain.Migration;

public sealed record SourceRecording
{
    public SourceRecording(
        long id,
        string name,
        DateTime startAt,
        DateTime endAt,
        ServiceKey? service,
        EventId? programme)
    {
        ArgumentNullException.ThrowIfNull(name);

        Id = SourceRow.Of(id, nameof(id));
        Name = name;
        StartAt = UtcTimes.Required(startAt, nameof(startAt));
        EndAt = UtcTimes.Required(endAt, nameof(endAt));

        if (EndAt < StartAt)
        {
            throw new ArgumentException("A recording ends after it starts.", nameof(endAt));
        }

        Service = service;
        Programme = programme;
    }

    public long Id { get; }

    public string Name { get; }

    public DateTime StartAt { get; }

    public DateTime EndAt { get; }

    public ServiceKey? Service { get; }

    public EventId? Programme { get; }
}
