using System.Globalization;
using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static class RecordingLeadRules
{
    public const string RecorderSettings = "Carina.Infrastructure/Recordings/RecordingSettings.cs";

    public const int SecondsTheDriverWaitsForALock = 5;

    public const int SecondsTheDriverWaitsForTheFirstByte = 5;

    public const int SecondsTheDriverWaitsForASeat = 10;

    public static IReadOnlyList<string> WhereTheHeadDisagreesWithTheDriver(string directory)
    {
        string recorder = Read(directory, RecorderSettings);
        List<string> disagreements = [];

        Compare(disagreements, "the lock", SecondsTheDriverWaitsForALock, Named(recorder, "WaitingForALock"));
        Compare(disagreements, "the first byte", SecondsTheDriverWaitsForTheFirstByte, Named(recorder, "WaitingForTheFirstByte"));
        Compare(disagreements, "a seat", SecondsTheDriverWaitsForASeat, Named(recorder, "WaitingForASeat"));

        return disagreements;
    }

    private static void Compare(List<string> disagreements, string what, int driver, int? recorder)
    {
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

    private static string Read(string directory, string relative)
        => File.ReadAllText(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)));
}
