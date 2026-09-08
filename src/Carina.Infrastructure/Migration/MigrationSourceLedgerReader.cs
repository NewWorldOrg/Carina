using System.Data.Common;
using System.Globalization;
using System.Text.Json;

using Carina.Domain.Migration;
using Carina.Domain.Programmes;

namespace Carina.Infrastructure.Migration;

public sealed class MigrationSourceLedgerReader(IMigrationSourceConnection connections) : IMigrationSourceLedger
{
    public const string OnlyReading = "START TRANSACTION READ ONLY";

    public const string Done = "COMMIT";

    public static readonly MigrationSourceName Source = new("the recording system being replaced");

    public static readonly IReadOnlyList<string> Populations =
        ["channel", "recorded", "video_file", "rule", "reserve"];

    private const bool NotTurnedOffAtTheSource = true;

    private const long ServicesPerNetwork = 100_000;

    private const long EventsPerService = 100_000;

    private const string ChannelDefinitions =
        "SELECT id, name, channelType, networkId, serviceId FROM channel ORDER BY id";

    private const string Recordings =
        "SELECT id, name, startAt, endAt, channelId, programId FROM recorded ORDER BY id";

    private const string RecordingFiles =
        "SELECT recordedId, parentDirectoryName, filePath, type, size FROM video_file ORDER BY id";

    private const string Rules =
        "SELECT id, keyword, enable, channelIds, keyCS, ignoreKeyCS, keyRegExp, ignoreKeyRegExp, "
        + "isTimeSpecification, times, durationMin, durationMax, searchPeriods, parentDirectoryName, "
        + "directory, mode1, mode2, mode3 FROM rule ORDER BY id";

    private const string Reservations = "SELECT id, name, halfWidthName, ruleId FROM reserve ORDER BY id";

    public async Task<SourceLedger> ReadAsync(CancellationToken cancellationToken)
    {
        DbConnection connection = await OpenedAsync(cancellationToken);

        await using (connection)
        {
            await SaysAsync(connection, OnlyReading, cancellationToken);

            IReadOnlyList<SourceChannelDefinition> channels = await ReadAsync(
                connection,
                ChannelDefinitions,
                MigrationPopulation.ChannelDefinitions,
                ChannelDefinitionOf,
                cancellationToken);

            Dictionary<long, ServiceKey> services = channels.ToDictionary(
                definition => definition.Id,
                definition => definition.Service);

            IReadOnlyList<SourceRecording> recordings = await ReadAsync(
                connection,
                Recordings,
                MigrationPopulation.Recordings,
                row => RecordingOf(row, services),
                cancellationToken);

            IReadOnlyList<ClaimedFile> claimed = await ReadAsync(
                connection,
                RecordingFiles,
                MigrationPopulation.RecordingFiles,
                ClaimedFileOf,
                cancellationToken);

            IReadOnlyList<SourceRule> rules = await ReadAsync(
                connection,
                Rules,
                MigrationPopulation.Rules,
                RuleOf,
                cancellationToken);

            IReadOnlyList<SourceReservation> reservations = await ReadAsync(
                connection,
                Reservations,
                MigrationPopulation.Reservations,
                ReservationOf,
                cancellationToken);

            await SaysAsync(connection, Done, cancellationToken);

            return new SourceLedger(
                Source,
                recordings,
                UnderOneRoot(claimed),
                rules,
                reservations,
                channels);
        }
    }

    private static IReadOnlyList<SourceRecordingFile> UnderOneRoot(IReadOnlyList<ClaimedFile> claimed)
    {
        IReadOnlyList<string> roots = [.. claimed
            .Select(file => file.Parent)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        return roots.Count > 1
            ? throw new MigrationSourceUnreadableException(
                $"The system being replaced keeps its recordings under {roots.Count} output directories, and a "
                + "run is given one.")
            : [.. claimed.Select(file => file.File)];
    }

    private static async Task SaysAsync(
        DbConnection connection,
        string statement,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = statement;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SourceChannelDefinition ChannelDefinitionOf(DbDataReader row)
        => new(
            Number(row, "id"),
            Text(row, "name"),
            KindOf(Text(row, "channelType")),
            KeyOf(Number(row, "networkId"), Number(row, "serviceId")),
            NotTurnedOffAtTheSource);

    private static SourceRecording RecordingOf(DbDataReader row, IReadOnlyDictionary<long, ServiceKey> services)
    {
        long channel = Number(row, "channelId");

        return new SourceRecording(
            Number(row, "id"),
            Text(row, "name"),
            Moment(Number(row, "startAt")),
            Moment(Number(row, "endAt")),
            services.GetValueOrDefault(channel),
            ProgrammeOf(NumberOrNone(row, "programId"), channel));
    }

    private static ClaimedFile ClaimedFileOf(DbDataReader row)
        => new(
            Text(row, "parentDirectoryName"),
            new SourceRecordingFile(
                Number(row, "recordedId"),
                Text(row, "filePath"),
                FileKindOf(Text(row, "type")),
                Number(row, "size")));

    private static SourceRule RuleOf(DbDataReader row)
        => new(
            Number(row, "id"),
            Text(row, "keyword"),
            Flag(row, "enable"),
            [.. Identifiers(Text(row, "channelIds")).Select(ServiceOf)],
            Flag(row, "keyRegExp") || Flag(row, "ignoreKeyRegExp"),
            Flag(row, "keyCS") || Flag(row, "ignoreKeyCS"),
            Flag(row, "isTimeSpecification") || NamesAnHourOfTheDay(Text(row, "times")),
            Whatever(row, "durationMin") > 0 || Whatever(row, "durationMax") > 0,
            Entries(Text(row, "searchPeriods")) > 0,
            Text(row, "parentDirectoryName").Length > 0 || Text(row, "directory").Length > 0,
            Text(row, "mode1").Length > 0 || Text(row, "mode2").Length > 0 || Text(row, "mode3").Length > 0);

    private static SourceReservation ReservationOf(DbDataReader row)
        => new(Number(row, "id"), NameOf(row), NumberOrNone(row, "ruleId") is not null);

    private static string NameOf(DbDataReader row)
    {
        string said = Text(row, "name");

        return said.Length > 0 ? said : Text(row, "halfWidthName");
    }

    private static ServiceKey ServiceOf(long channel)
        => KeyOf(channel / ServicesPerNetwork, channel % ServicesPerNetwork);

    private static ServiceKey KeyOf(long network, long service)
        => ServiceKey.Of(checked((int)network), checked((int)service));

    private static EventId? ProgrammeOf(long? programme, long channel)
    {
        if (programme is not { } identifier)
        {
            return null;
        }

        long carried = identifier - (channel * EventsPerService);

        return carried is >= EventId.MinValue and <= EventId.MaxValue ? new EventId((int)carried) : null;
    }

    private static DateTime Moment(long milliseconds)
        => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;

    private static SourceFileKind FileKindOf(string said)
        => said switch
        {
            "ts" => SourceFileKind.AsBroadcast,
            "encoded" => SourceFileKind.Encoded,
            _ => throw new ArgumentException(
                $"A file of the system being replaced is one it can name, and '{said}' is not one of them."),
        };

    private static SourceBroadcastKind KindOf(string said)
        => said switch
        {
            "GR" => SourceBroadcastKind.Terrestrial,
            "BS" => SourceBroadcastKind.BroadcastSatellite,
            "CS" => SourceBroadcastKind.CommunicationSatellite,
            "SKY" => SourceBroadcastKind.Sky,
            _ => throw new ArgumentException(
                $"A channel of the system being replaced is one it can name, and '{said}' is not one of them."),
        };

    private static IReadOnlyList<long> Identifiers(string json)
    {
        if (json.Length is 0)
        {
            return [];
        }

        using JsonDocument held = JsonDocument.Parse(json);

        return [.. held.RootElement.EnumerateArray().Select(element => element.GetInt64())];
    }

    private static bool NamesAnHourOfTheDay(string json)
    {
        if (json.Length is 0)
        {
            return false;
        }

        using JsonDocument held = JsonDocument.Parse(json);

        return held.RootElement.EnumerateArray().Any(entry => entry.TryGetProperty("start", out JsonElement _));
    }

    private static int Entries(string json)
    {
        if (json.Length is 0)
        {
            return 0;
        }

        using JsonDocument held = JsonDocument.Parse(json);

        return held.RootElement.GetArrayLength();
    }

    private static long Number(DbDataReader row, string column)
        => Convert.ToInt64(row[column], CultureInfo.InvariantCulture);

    private static long? NumberOrNone(DbDataReader row, string column)
        => row[column] is DBNull ? null : Number(row, column);

    private static long Whatever(DbDataReader row, string column) => NumberOrNone(row, column) ?? 0;

    private static string Text(DbDataReader row, string column) => row[column] as string ?? string.Empty;

    private static bool Flag(DbDataReader row, string column) => Whatever(row, column) is not 0;

    private static string Said(Exception failure)
        => failure is DbException ? failure.GetType().Name : failure.Message;

    private async Task<DbConnection> OpenedAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await connections.OpenAsync(cancellationToken);
        }
        catch (Exception refused)
            when (refused is not (MigrationSourceUnreadableException or OperationCanceledException))
        {
            throw new MigrationSourceUnreadableException(
                "The system being replaced could not be reached with what "
                + $"{MigrationSourceSettings.ConnectionVariable} holds ({refused.GetType().Name}).");
        }
    }

    private async Task<IReadOnlyList<T>> ReadAsync<T>(
        DbConnection connection,
        string statement,
        MigrationPopulation population,
        Func<DbDataReader, T> of,
        CancellationToken cancellationToken)
    {
        List<T> found = [];

        try
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = statement;

            await using DbDataReader rows = await command.ExecuteReaderAsync(cancellationToken);

            while (await rows.ReadAsync(cancellationToken))
            {
                found.Add(of(rows));
            }
        }
        catch (Exception unreadable) when (unreadable
            is DbException
            or ArgumentException
            or InvalidOperationException
            or FormatException
            or JsonException
            or OverflowException
            or IndexOutOfRangeException)
        {
            throw new MigrationSourceUnreadableException(
                $"The {population} of the system being replaced could not be read: {Said(unreadable)}");
        }

        return found;
    }

    private sealed record ClaimedFile(string Parent, SourceRecordingFile File);
}
