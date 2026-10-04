using Microsoft.Extensions.Logging.Console;

namespace Carina.Api.Extensions;

public static class LoggingBuilderExtensions
{
    public const string LineTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z' ";

    public static readonly IReadOnlyList<string> CategoriesThatNameTheRequestPath =
    [
        "Microsoft.AspNetCore.Hosting.Diagnostics",
        "Microsoft.AspNetCore.Routing.Matching",
    ];

    /// <summary>
    /// Has every line written to the console begin with the moment it was written, in UTC.
    /// </summary>
    public static ILoggingBuilder AddConsoleLinesWithTheirTime(this ILoggingBuilder logging)
    {
        ArgumentNullException.ThrowIfNull(logging);

        return logging.AddSimpleConsole(line =>
        {
            line.TimestampFormat = LineTimeFormat;
            line.UseUtcTimestamp = true;
        });
    }

    /// <summary>
    /// Keeps the framework from writing the lines that name the path a request came on, whatever level is configured.
    /// </summary>
    public static ILoggingBuilder KeepRequestLinesOut(this ILoggingBuilder logging)
    {
        ArgumentNullException.ThrowIfNull(logging);

        logging.Services.PostConfigure<LoggerFilterOptions>(KeepRequestLinesOut);

        return logging;
    }

    private static void KeepRequestLinesOut(LoggerFilterOptions options)
    {
        LoggerFilterRule[] loosening = [.. options.Rules.Where(NamesTheRequestPath)];
        string?[] providers =
        [
            null,
            .. options.Rules.Select(rule => rule.ProviderName).OfType<string>().Distinct(StringComparer.Ordinal),
        ];

        foreach (LoggerFilterRule rule in loosening)
        {
            options.Rules.Remove(rule);
            options.Rules.Add(new LoggerFilterRule(rule.ProviderName, rule.CategoryName, AtMostWarnings(rule.LogLevel), rule.Filter));
        }

        foreach (string? provider in providers)
        {
            foreach (string category in CategoriesThatNameTheRequestPath)
            {
                options.Rules.Add(new LoggerFilterRule(provider, category, LogLevel.Warning, null));
            }
        }
    }

    private static bool NamesTheRequestPath(LoggerFilterRule rule)
        => rule.CategoryName is { } category
           && CategoriesThatNameTheRequestPath.Any(kept => category.StartsWith(kept, StringComparison.OrdinalIgnoreCase));

    private static LogLevel AtMostWarnings(LogLevel? level)
        => level is { } said && said > LogLevel.Warning ? said : LogLevel.Warning;
}
