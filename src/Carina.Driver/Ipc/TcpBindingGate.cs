using Microsoft.Extensions.Configuration;

namespace Carina.Driver.Ipc;

public static class TcpBindingGate
{
    public const string UrlsVariable = "ASPNETCORE_URLS";

    public const string UrlsSetting = "urls";

    public const string UrlsArgument = "--urls";

    public const string EndpointsSection = "Kestrel:Endpoints";

    private const string Reason =
        "The driver answers on a Unix socket only and never binds a TCP port.";

    public static IReadOnlyList<string> Inspect(
        IConfiguration configuration,
        IReadOnlyList<string> args,
        Func<string, string?>? environment = null
    )
    {
        var read = environment ?? Environment.GetEnvironmentVariable;
        var findings = new List<string>();

        if (read(UrlsVariable) is { Length: > 0 } fromEnvironment)
        {
            findings.Add($"{UrlsVariable} is set to '{fromEnvironment}'. {Reason}");
        }

        foreach (var argument in args)
        {
            if (
                string.Equals(argument, UrlsArgument, StringComparison.Ordinal)
                || argument.StartsWith($"{UrlsArgument}=", StringComparison.Ordinal)
            )
            {
                findings.Add($"'{argument}' was passed on the command line. {Reason}");
            }
        }

        if (configuration[UrlsSetting] is { Length: > 0 } fromConfiguration)
        {
            findings.Add($"the '{UrlsSetting}' setting reads '{fromConfiguration}'. {Reason}");
        }

        foreach (var endpoint in configuration.GetSection(EndpointsSection).GetChildren())
        {
            findings.Add(
                $"'{EndpointsSection}:{endpoint.Key}' names an endpoint ({endpoint["Url"] ?? "with no url"}). {Reason}"
            );
        }

        return findings;
    }
}
