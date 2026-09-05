using System.Security.Cryptography;
using System.Text;

using Carina.Domain.Auth;

namespace Carina.TestSupport;

public sealed class QuickPasswordHasher : IPasswordHasher
{
    public PasswordHash Hash(string password, PasswordHashPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        byte[] salt = RandomNumberGenerator.GetBytes(policy.SaltLength);

        return PasswordHash.Encode(policy, salt, Derive(password, salt, policy.DigestLength));
    }

    public bool Matches(string password, PasswordHash hash)
    {
        ArgumentNullException.ThrowIfNull(hash);

        return hash.Matches(Derive(password, hash.CopySalt(), hash.DigestLength));
    }

    private static byte[] Derive(string password, byte[] salt, int digestLength)
        => SHA512.HashData([.. salt, .. Encoding.UTF8.GetBytes(password)])[..digestLength];
}

public sealed class HeldAuthSessions : IAuthSessionRepository
{
    public List<AuthSession> Sessions { get; } = [];

    public int Deletions { get; private set; }

    public Task<AuthSession?> FindAsync(SessionId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return Task.FromResult(Sessions.FirstOrDefault(session => session.Id.Equals(id)));
    }

    public Task<IReadOnlyList<AuthSession>> ListAsync(Subject subject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);

        return Task.FromResult<IReadOnlyList<AuthSession>>(
            [.. Sessions.Where(session => session.Subject.Equals(subject))]);
    }

    public Task<IReadOnlyList<AuthSession>> ListAllAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<AuthSession>>([.. Sessions.OrderByDescending(session => session.LastUsedAt)]);

    public Task SaveAsync(AuthSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!Sessions.Any(held => held.Id.Equals(session.Id)))
        {
            Sessions.Add(session);
        }

        return Task.CompletedTask;
    }

    public Task SaveAllAsync(IReadOnlyList<AuthSession> sessions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        foreach (AuthSession session in sessions)
        {
            if (!Sessions.Any(held => held.Id.Equals(session.Id)))
            {
                Sessions.Add(session);
            }
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(SessionId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        Deletions += Sessions.RemoveAll(session => session.Id.Equals(id));

        return Task.CompletedTask;
    }
}

public sealed class HeldLocalAccount : ILocalAccountRepository
{
    public LocalAccount? Account { get; set; }

    public int Saves { get; private set; }

    public Task<LocalAccount?> FindAsync(CancellationToken cancellationToken) => Task.FromResult(Account);

    public Task SaveAsync(LocalAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        Account = account;
        Saves++;

        return Task.CompletedTask;
    }
}

public sealed class CountingPasswordHasher(IPasswordHasher inner) : IPasswordHasher
{
    public int Derivations { get; private set; }

    public void Reset() => Derivations = 0;

    public PasswordHash Hash(string password, PasswordHashPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(inner);

        Derivations++;

        return inner.Hash(password, policy);
    }

    public bool Matches(string password, PasswordHash hash)
    {
        ArgumentNullException.ThrowIfNull(inner);

        Derivations++;

        return inner.Matches(password, hash);
    }
}

public sealed class HeldOidcSettings : IOidcSettingsRepository
{
    public OidcSettings? Settings { get; set; }

    public int Saves { get; private set; }

    public Task<OidcSettings?> FindAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);

    public Task SaveAsync(OidcSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Settings = settings;
        Saves++;

        return Task.CompletedTask;
    }
}

public sealed class WoundClock(DateTimeOffset from) : TimeProvider
{
    private DateTimeOffset now = from;

    public override DateTimeOffset GetUtcNow() => now;

    public void Wind(TimeSpan by) => now = now.Add(by);
}
