using Carina.Broadcast.Descriptors;

namespace Carina.Broadcast.Tables;

public sealed class ElementaryStream
{
    internal ElementaryStream(int streamType, int pid, IReadOnlyList<Descriptor> descriptors)
    {
        StreamType = streamType;
        Pid = pid;
        Descriptors = descriptors;
    }

    public int StreamType { get; }

    public int Pid { get; }

    public IReadOnlyList<Descriptor> Descriptors { get; }
}
