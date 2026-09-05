namespace Carina.Domain.Auth;

public interface IAuthSessionRepository
{
    Task<AuthSession?> FindAsync(SessionId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuthSession>> ListAsync(Subject subject, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuthSession>> ListAllAsync(CancellationToken cancellationToken);

    Task SaveAsync(AuthSession session, CancellationToken cancellationToken);

    Task SaveAllAsync(IReadOnlyList<AuthSession> sessions, CancellationToken cancellationToken);

    Task DeleteAsync(SessionId id, CancellationToken cancellationToken);
}
