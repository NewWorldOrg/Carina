using System.Globalization;
using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class RecordingLeadRules
{
    public const string DriverSettings = "Carina.Driver/Tuning/Dvb/DvbTunerDevice.cs";

    public const string DriverSessions = "Carina.Driver/Sessions/TunerSessionManager.cs";

    public const string RecorderSettings = "Carina.Infrastructure/Recordings/RecordingSettings.cs";

    public static IReadOnlyList<string> WhereTheHeadDisagreesWithTheDriver(string directory)
    {
        string settings = Read(directory, DriverSettings);
        string sessions = Read(directory, DriverSessions);
        string recorder = Read(directory, RecorderSettings);

        Match tuner = DvbDefaults().Match(settings);

        if (!tuner.Success)
        {
            return [$"{DriverSettings}: the shape of DvbTunerSettings.Default is not one this rule can read."];
        }

        int[] waits = [.. Seconds().Matches(tuner.Value).Select(match => Number(match))];

        if (waits.Length is not 2)
        {
            return
            [
                $"{DriverSettings}: DvbTunerSettings.Default names {waits.Length} waits in whole seconds, "
                + "and this rule reads the lock and the first byte from exactly two.",
            ];
        }

        List<string> disagreements = [];

        Compare(disagreements, "the lock", waits[0], Named(recorder, "WaitingForALock"));
        Compare(disagreements, "the first byte", waits[1], Named(recorder, "WaitingForTheFirstByte"));
        Compare(disagreements, "a seat", Named(sessions, "HandOverLimit"), Named(recorder, "WaitingForASeat"));

        return disagreements;
    }

    private static void Compare(List<string> disagreements, string what, int? driver, int? recorder)
    {
        if (driver is null)
        {
            disagreements.Add($"the driver no longer says how long it waits for {what} in whole seconds.");

            return;
        }

        if (recorder is null)
        {
            disagreements.Add($"{RecorderSettings} no longer says how long it allows for {what}.");

            return;
        }

        if (driver != recorder)
        {
            disagreements.Add(
                $"the driver waits {driver}s for {what} and the head the recorder allows says {recorder}s.");
        }
    }

    private static int? Named(string source, string name)
    {
        Match found = Regex.Match(
            source,
            name + @"\s*=\s*TimeSpan\.FromSeconds\((\d+)\)",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        return found.Success ? int.Parse(found.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    private static int Number(Match match)
        => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);

    private static string Read(string directory, string relative)
        => File.ReadAllText(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)));

    [GeneratedRegex(@"DvbTunerSettings\s+Default\s*=\s*new\((?<body>[^;]*?)\);", RegexOptions.Singleline)]
    private static partial Regex DvbDefaults();

    [GeneratedRegex(@"TimeSpan\.FromSeconds\((\d+)\)")]
    private static partial Regex Seconds();
}
