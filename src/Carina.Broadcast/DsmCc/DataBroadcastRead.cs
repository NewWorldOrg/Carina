namespace Carina.Broadcast.DsmCc;

/// <summary>
/// What a <see cref="DataBroadcastTap"/> read of a programme's data broadcast, at the moment of the
/// programme's clock, followed through its wrap, it was read at.
/// </summary>
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
        internal EventMessageTimed(long at, TimedEventMessage message, long firesAt)
            : base(at)
        {
            Message = message;
            FiresAt = firesAt;
        }

        public TimedEventMessage Message { get; }

        /// <summary>
        /// When the message fires, on the same clock as <see cref="DataBroadcastRead.At"/>.
        /// </summary>
        public long FiresAt { get; }
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
