using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;

namespace Carina.Api.Responder.Epg;

public sealed record BoostStartedResponder(Guid BoostId, int Streams)
{
    public static BoostStartedResponder Of(BoostStarted started)
    {
        ArgumentNullException.ThrowIfNull(started);

        return new BoostStartedResponder(started.BoostId, started.Streams);
    }
}

public sealed record BoostRefusedResponder(
    BoostRefusal Refusal,
    Guid? RunningBoostId,
    DateTimeOffset? NotBefore)
{
    public static BoostRefusedResponder Of(BoostVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        return new BoostRefusedResponder(
            verdict.Refusal,
            verdict.RunningId,
            verdict.NotBefore is null ? null : new DateTimeOffset(verdict.NotBefore.Value, TimeSpan.Zero));
    }
}
