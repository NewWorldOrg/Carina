namespace Carina.Broadcast.DsmCc;

public abstract record EventMessageOutcome
{
    private EventMessageOutcome()
    {
    }

    public sealed record Timed : EventMessageOutcome
    {
        internal Timed(TimedEventMessage message)
        {
            Message = message;
        }

        public TimedEventMessage Message { get; }
    }

    public sealed record Discarded : EventMessageOutcome
    {
        internal Discarded(EventMessageDefect defect)
        {
            Defect = defect;
        }

        public EventMessageDefect Defect { get; }
    }
}
