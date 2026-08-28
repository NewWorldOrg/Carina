namespace Carina.Domain.Reservations;

public enum RecalculationTrigger
{
    ReservationChanged = 1,

    RulesChanged = 2,

    ProgrammesChanged = 3,

    SelectedChannelChanged = 4,

    TunerConfigurationChanged = 5,

    TunerFaulted = 6,

    RecordingStarted = 7,

    RecordingExtended = 8,

    RecordingEnded = 9,

    PeriodicReconciliation = 10,

    AppStarted = 11,
}
