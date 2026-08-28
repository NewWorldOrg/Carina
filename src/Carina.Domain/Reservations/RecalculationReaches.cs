namespace Carina.Domain.Reservations;

public static class RecalculationReaches
{
    private static readonly IReadOnlyDictionary<RecalculationTrigger, RecalculationReach> Table =
        new Dictionary<RecalculationTrigger, RecalculationReach>
        {
            [RecalculationTrigger.ReservationChanged] = RecalculationReach.Nothing,
            [RecalculationTrigger.TunerFaulted] = RecalculationReach.Nothing,
            [RecalculationTrigger.SelectedChannelChanged] = RecalculationReach.Settle,
            [RecalculationTrigger.TunerConfigurationChanged] = RecalculationReach.Settle,
            [RecalculationTrigger.RecordingStarted] = RecalculationReach.Settle,
            [RecalculationTrigger.RecordingExtended] = RecalculationReach.Settle,
            [RecalculationTrigger.RecordingEnded] = RecalculationReach.Settle,
            [RecalculationTrigger.ProgrammesChanged] = RecalculationReach.Increment,
            [RecalculationTrigger.RulesChanged] = RecalculationReach.Everything,
            [RecalculationTrigger.PeriodicReconciliation] = RecalculationReach.Everything,
            [RecalculationTrigger.AppStarted] = RecalculationReach.Everything,
        };

    public static IReadOnlyList<RecalculationTrigger> Declared => [.. Table.Keys.Order()];

    public static RecalculationReach Of(RecalculationTrigger trigger)
        => Table.TryGetValue(trigger, out RecalculationReach reach)
            ? reach
            : throw new ArgumentOutOfRangeException(
                nameof(trigger),
                trigger,
                "A trigger says how far the recalculation it asks for reaches, and this one says nothing.");

    public static RecalculationReach Widest(IEnumerable<RecalculationTrigger> triggers)
    {
        ArgumentNullException.ThrowIfNull(triggers);

        RecalculationReach widest = RecalculationReach.Nothing;

        foreach (RecalculationTrigger trigger in triggers)
        {
            RecalculationReach reach = Of(trigger);

            if (reach > widest)
            {
                widest = reach;
            }
        }

        return widest;
    }
}
