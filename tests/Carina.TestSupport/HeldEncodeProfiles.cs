using Carina.Domain.Encodings;

namespace Carina.TestSupport;

public sealed class HeldEncodeProfiles : IEncodeProfileRepository
{
    public List<EncodeProfile> Profiles { get; } = [];

    public Task<EncodeProfile?> FindAsync(EncodeProfileId id, CancellationToken cancellationToken)
        => Task.FromResult(Profiles.FirstOrDefault(profile => profile.Id.Equals(id)));

    public Task<IReadOnlyList<EncodeProfile>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<EncodeProfile> listed = [.. Profiles.OrderBy(profile => profile.DefinedAt)];

        return Task.FromResult(listed);
    }

    public Task AddAsync(EncodeProfile profile, CancellationToken cancellationToken)
    {
        Profiles.Add(profile);

        return Task.CompletedTask;
    }

    public Task SaveAsync(EncodeProfile profile, CancellationToken cancellationToken)
    {
        if (!Profiles.Contains(profile))
        {
            Profiles.Add(profile);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(EncodeProfile profile, CancellationToken cancellationToken)
    {
        Profiles.Remove(profile);

        return Task.CompletedTask;
    }
}
