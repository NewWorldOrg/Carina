using Carina.Domain.Auth;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class AuthSessionRepository(CarinaDbContext context) : IAuthSessionRepository
{
    public async Task<AuthSession?> FindAsync(SessionId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<AuthSession>()
            .FirstOrDefaultAsync(session => session.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<AuthSession>> ListAsync(Subject subject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);

        return await context.Set<AuthSession>()
            .Where(session => session.Subject == subject)
            .OrderByDescending(session => session.LastUsedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(AuthSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        Hold(session);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAllAsync(IReadOnlyList<AuthSession> sessions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        foreach (AuthSession session in sessions)
        {
            Hold(session);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(SessionId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        AuthSession? held = await FindAsync(id, cancellationToken);

        if (held is null)
        {
            return;
        }

        context.Set<AuthSession>().Remove(held);

        await context.SaveChangesAsync(cancellationToken);
    }

    private void Hold(AuthSession session)
    {
        if (context.Entry(session).State is EntityState.Detached)
        {
            context.Set<AuthSession>().Add(session);
        }
    }
}
