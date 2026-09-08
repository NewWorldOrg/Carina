using Carina.Domain.Channels;

namespace Carina.Domain.Migration;

public sealed class MigrationChannelProposal
{
    public const int NameMaxLength = 256;

    private MigrationChannelProposal()
    {
    }

    public MigrationRunId RunId { get; private set; } = null!;

    public NetworkId NetworkId { get; private set; } = null!;

    public ServiceId ServiceId { get; private set; } = null!;

    public MigrationChannelStanding Standing { get; private set; }

    public string SourceName { get; private set; } = string.Empty;

    public string SourcePhysicalChannel { get; private set; } = string.Empty;

    public string? RescannedName { get; private set; }

    public static MigrationChannelProposal Rehydrate(
        MigrationRunId runId,
        NetworkId networkId,
        ServiceId serviceId,
        MigrationChannelStanding standing,
        string sourceName,
        string sourcePhysicalChannel,
        string? rescannedName)
    {
        ArgumentNullException.ThrowIfNull(runId);
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentNullException.ThrowIfNull(sourceName);
        ArgumentNullException.ThrowIfNull(sourcePhysicalChannel);

        MigrationChannelStanding named = MigrationChannelStandings.Named(standing);

        if ((named is MigrationChannelStanding.NameProposed) != (rescannedName is not null))
        {
            throw new ArgumentException(
                "A name is proposed for a service the rescan answers for, and for no other.",
                nameof(rescannedName));
        }

        return new MigrationChannelProposal
        {
            RunId = runId,
            NetworkId = networkId,
            ServiceId = serviceId,
            Standing = named,
            SourceName = Held(sourceName, nameof(sourceName)),
            SourcePhysicalChannel = Held(sourcePhysicalChannel, nameof(sourcePhysicalChannel)),
            RescannedName = rescannedName is null ? null : Held(rescannedName, nameof(rescannedName)),
        };
    }

    public static IReadOnlyList<MigrationChannelProposal> Over(
        MigrationRunId runId,
        IReadOnlyList<SourceChannelDefinition> definitions,
        IReadOnlyDictionary<ServiceKey, string> rescanned)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(rescanned);

        List<MigrationChannelProposal> found = [];

        foreach (SourceChannelDefinition definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition, nameof(definitions));

            bool answered = rescanned.TryGetValue(definition.Service, out string? name);
            bool sayable = definition.Kind is not SourceBroadcastKind.Sky;

            found.Add(Rehydrate(
                runId,
                definition.Service.Network,
                definition.Service.Service,
                (sayable, answered) switch
                {
                    (false, _) => MigrationChannelStanding.Inexpressible,
                    (true, true) => MigrationChannelStanding.NameProposed,
                    _ => MigrationChannelStanding.NothingAnswers,
                },
                definition.Name,
                definition.PhysicalChannel,
                sayable && answered ? name : null));
        }

        return found;
    }

    private static string Held(string said, string parameterName)
    {
        string kept = MigrationNote.Of(said);

        return kept.Length <= NameMaxLength
            ? kept
            : throw new ArgumentException(
                $"A name is at most {NameMaxLength} characters, but this one has {kept.Length}.",
                parameterName);
    }
}
