using System.Globalization;

using Carina.Domain.Auth;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Configuration;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    private const int MostFailures = 100;

    private static readonly TimeSpan LongestSession = TimeSpan.FromDays(365);

    private static readonly TimeSpan LongestLoginWindow = TimeSpan.FromDays(1);

    public string? SessionAbsoluteLifetime { get; set; }

    public string? SessionIdleTimeout { get; set; }

    public string? SessionBetweenLastUsedWrites { get; set; }

    public string? LoginFailuresBeforeRefusing { get; set; }

    public string? LoginWindow { get; set; }

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection named = configuration.GetSection(Section);

        SessionAbsoluteLifetime = named[nameof(SessionAbsoluteLifetime)];
        SessionIdleTimeout = named[nameof(SessionIdleTimeout)];
        SessionBetweenLastUsedWrites = named[nameof(SessionBetweenLastUsedWrites)];
        LoginFailuresBeforeRefusing = named[nameof(LoginFailuresBeforeRefusing)];
        LoginWindow = named[nameof(LoginWindow)];
    }

    public SessionPolicy ReadSession()
    {
        SessionPolicy unset = SessionPolicy.Default;

        TimeSpan lifetime = Spanning(
            SessionAbsoluteLifetime,
            nameof(SessionAbsoluteLifetime),
            unset.AbsoluteLifetime,
            LongestSession,
            "which is as long as a session may sit in the ledger before anything forgets it");

        TimeSpan idle = Spanning(
            SessionIdleTimeout,
            nameof(SessionIdleTimeout),
            unset.IdleTimeout,
            LongestSession,
            "which is as long as a session may sit in the ledger before anything forgets it");

        if (idle > lifetime)
        {
            throw new ArgumentException(
                $"{Section}:{nameof(SessionIdleTimeout)} cannot outlast "
                + $"{Section}:{nameof(SessionAbsoluteLifetime)}, and '{idle}' outlasts '{lifetime}'.",
                nameof(SessionIdleTimeout));
        }

        TimeSpan between = Spanning(
            SessionBetweenLastUsedWrites,
            nameof(SessionBetweenLastUsedWrites),
            unset.BetweenLastUsedWrites,
            LongestSession,
            "which is as long as a session may sit in the ledger before anything forgets it");

        if (idle <= between)
        {
            throw new ArgumentException(
                $"{Section}:{nameof(SessionIdleTimeout)} has to be longer than the "
                + $"{between} between the writes that say a session was used, "
                + "or a session somebody is still asking with would be forgotten under them.",
                nameof(SessionIdleTimeout));
        }

        return new SessionPolicy(lifetime, idle, between);
    }

    public LoginRatePolicy ReadLogin()
    {
        LoginRatePolicy unset = LoginRatePolicy.Default;

        return new LoginRatePolicy(
            Counted(LoginFailuresBeforeRefusing, nameof(LoginFailuresBeforeRefusing), unset.FailuresBeforeRefusing),
            Spanning(
                LoginWindow,
                nameof(LoginWindow),
                unset.Window,
                LongestLoginWindow,
                "past which a wrong password is still held against the caller who typed it"));
    }

    private static TimeSpan Spanning(string? setting, string name, TimeSpan unset, TimeSpan longest, string why)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        TimeSpan read = TimeSpan.TryParse(setting, CultureInfo.InvariantCulture, out TimeSpan parsed)
            ? parsed
            : throw new ArgumentException(
                $"{Section}:{name} reads a duration as [d.]hh:mm:ss, and '{setting}' is not one.",
                name);

        if (read <= TimeSpan.Zero)
        {
            throw new ArgumentException($"{Section}:{name} has to be longer than nothing.", name);
        }

        return read <= longest
            ? read
            : throw new ArgumentException($"{Section}:{name} cannot be longer than {longest}, {why}.", name);
    }

    private static int Counted(string? setting, string name, int unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        int read = int.TryParse(setting, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new ArgumentException(
                $"{Section}:{name} reads a whole number, and '{setting}' is not one.",
                name);

        if (read < 1)
        {
            throw new ArgumentException($"{Section}:{name} has to count at least one wrong password.", name);
        }

        return read <= MostFailures
            ? read
            : throw new ArgumentException(
                $"{Section}:{name} cannot be more than {MostFailures}, past which the refusal stops "
                + "standing between a caller and every password there is.",
                name);
    }
}

public sealed class AuthValidation : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            options.ReadSession();
            options.ReadLogin();
        }
        catch (ArgumentException refusal)
        {
            return ValidateOptionsResult.Fail(refusal.Message);
        }

        return ValidateOptionsResult.Success;
    }
}
