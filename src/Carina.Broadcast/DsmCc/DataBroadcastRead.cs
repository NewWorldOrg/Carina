namespace Carina.Broadcast.DsmCc;

public abstract record DataBroadcastRead
{
    private DataBroadcastRead(long at)
    {
        At = at;
    }

    public long At { get; }

    public sealed record ServiceMapped : DataBroadcastRead
    {
        internal ServiceMapped(long at, DataBroadcastService service)
            : base(at)
        {
            Service = service;
        }

        public DataBroadcastService Service { get; }
    }

    public sealed record CarouselChanged : DataBroadcastRead
    {
        internal CarouselChanged(long at, CarouselChange change)
            : base(at)
        {
            Change = change;
        }

        public CarouselChange Change { get; }
    }

    public sealed record EventMessageTimed : DataBroadcastRead
    {
        internal EventMessageTimed(long at, int componentTag, TimedEventMessage message, long firesAt)
            : base(at)
        {
            ComponentTag = componentTag;
            Message = message;
            FiresAt = firesAt;
        }

        public int ComponentTag { get; }

        public TimedEventMessage Message { get; }

        public long FiresAt { get; }
    }

    public sealed record NptReferred : DataBroadcastRead
    {
        internal NptReferred(long at, int componentTag, NptReference reference, long stc)
            : base(at)
        {
            ComponentTag = componentTag;
            Reference = reference;
            Stc = stc;
        }

        public int ComponentTag { get; }

        public NptReference Reference { get; }

        public long Stc { get; }
    }

    public sealed record EventMessageDiscarded : DataBroadcastRead
    {
        internal EventMessageDiscarded(long at, EventMessageDefect defect)
            : base(at)
        {
            Defect = defect;
        }

        public EventMessageDefect Defect { get; }
    }
}
