namespace Carina.Domain.Reservations;

public interface IRecalculationNotice
{
    void Nudge(RecalculationTrigger trigger);
}
