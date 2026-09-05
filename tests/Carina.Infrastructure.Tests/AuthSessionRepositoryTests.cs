using Carina.Domain.Auth;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class AuthSessionRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime At = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ASessionIsFoundByTheIdentifierTheCookieCarries()
    {
        AuthSession started = Started(new Subject("carina"), "a device");

        await using (CarinaDbContext writing = database.Open())
        {
            await new AuthSessionRepository(writing).SaveAsync(started, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        AuthSession? read = await new AuthSessionRepository(reading).FindAsync(started.Id, Cancel);

        Assert.NotNull(read);
        Assert.Equal(started.Id.Value, read.Id.Value);
        Assert.Equal("carina", read.Subject.Value);
        Assert.Equal("carina", read.DisplayName);
        Assert.Equal(AuthMethod.Local, read.Method);
        Assert.Equal("a device", read.DeviceLabel);
        Assert.Null(read.RevokedAt);
    }

    [Fact]
    public async Task AnIdentifierThatWasNeverIssuedFindsNothing()
    {
        await using CarinaDbContext reading = database.Open();

        Assert.Null(await new AuthSessionRepository(reading).FindAsync(SessionId.Issue(), Cancel));
    }

    [Fact]
    public async Task TheListForOneAccountLeavesAnotherAccountsSessionsWhereTheyAre()
    {
        AuthSession mine = Started(new Subject("carina"), "my device");
        AuthSession theirs = Started(new Subject("somebody-else"), "their device");

        await using (CarinaDbContext writing = database.Open())
        {
            var repository = new AuthSessionRepository(writing);
            await repository.SaveAsync(mine, Cancel);
            await repository.SaveAsync(theirs, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<AuthSession> listed = await new AuthSessionRepository(reading)
            .ListAsync(new Subject("carina"), Cancel);

        Assert.Contains(listed, session => session.Id.Value == mine.Id.Value);
        Assert.DoesNotContain(listed, session => session.Id.Value == theirs.Id.Value);
    }

    [Fact]
    public async Task BrAu018TheListOfEveryoneCarriesEveryAccountsSessionsMostRecentlyUsedFirst()
    {
        AuthSession mine = Started(new Subject("carina"), "my device");
        AuthSession theirs = AuthSession.Start(
            SessionId.Issue(),
            new Subject("108204329581372"),
            "somebody@example.test",
            AuthMethod.Oidc,
            "their device",
            At.AddMinutes(1));

        await using (CarinaDbContext writing = database.Open())
        {
            var repository = new AuthSessionRepository(writing);
            await repository.SaveAsync(mine, Cancel);
            await repository.SaveAsync(theirs, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<AuthSession> listed = await new AuthSessionRepository(reading).ListAllAsync(Cancel);

        int ofTheirs = listed.ToList().FindIndex(session => session.Id.Value == theirs.Id.Value);
        int ofMine = listed.ToList().FindIndex(session => session.Id.Value == mine.Id.Value);

        Assert.True(ofTheirs >= 0 && ofMine >= 0);
        Assert.True(ofTheirs < ofMine);
        Assert.Equal("somebody@example.test", listed[ofTheirs].DisplayName);
        Assert.Equal(AuthMethod.Oidc, listed[ofTheirs].Method);
    }

    [Fact]
    public async Task ATouchedSessionKeepsItsNewLastUsedTimeWhenItIsReadBack()
    {
        AuthSession started = Started(new Subject("carina"), "a device");

        await using (CarinaDbContext writing = database.Open())
        {
            await new AuthSessionRepository(writing).SaveAsync(started, Cancel);
        }

        await using (CarinaDbContext touching = database.Open())
        {
            var repository = new AuthSessionRepository(touching);
            AuthSession? held = await repository.FindAsync(started.Id, Cancel);

            Assert.True(held!.Touch(At.AddHours(1), SessionPolicy.Default));

            await repository.SaveAsync(held, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        AuthSession? read = await new AuthSessionRepository(reading).FindAsync(started.Id, Cancel);

        Assert.Equal(At.AddHours(1), read!.LastUsedAt);
    }

    [Fact]
    public async Task EveryOtherSessionSavedAtOnceComesBackRevokedWhileTheKeptOneDoesNot()
    {
        var subject = new Subject("carina");
        AuthSession here = Started(subject, "this device");
        AuthSession there = Started(subject, "another device");

        await using (CarinaDbContext writing = database.Open())
        {
            var repository = new AuthSessionRepository(writing);
            await repository.SaveAsync(here, Cancel);
            await repository.SaveAsync(there, Cancel);
        }

        await using (CarinaDbContext revoking = database.Open())
        {
            var repository = new AuthSessionRepository(revoking);
            IReadOnlyList<AuthSession> held = await repository.ListAsync(subject, Cancel);
            List<AuthSession> ended = [];

            foreach (AuthSession session in held)
            {
                if (!session.Id.Equals(here.Id) && session.Revoke(At.AddMinutes(1)))
                {
                    ended.Add(session);
                }
            }

            await repository.SaveAllAsync(ended, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        var read = new AuthSessionRepository(reading);

        Assert.Null((await read.FindAsync(here.Id, Cancel))!.RevokedAt);
        Assert.Equal(At.AddMinutes(1), (await read.FindAsync(there.Id, Cancel))!.RevokedAt);
    }

    [Fact]
    public async Task ADeletedSessionIsGoneRatherThanMerelyRevoked()
    {
        AuthSession started = Started(new Subject("carina"), "a device");

        await using (CarinaDbContext writing = database.Open())
        {
            await new AuthSessionRepository(writing).SaveAsync(started, Cancel);
        }

        await using (CarinaDbContext deleting = database.Open())
        {
            await new AuthSessionRepository(deleting).DeleteAsync(started.Id, Cancel);
        }

        await using CarinaDbContext reading = database.Open();

        Assert.Null(await new AuthSessionRepository(reading).FindAsync(started.Id, Cancel));
    }

    [Fact]
    public async Task DeletingASessionThatIsAlreadyGoneIsNotAFailure()
    {
        await using CarinaDbContext deleting = database.Open();

        await new AuthSessionRepository(deleting).DeleteAsync(SessionId.Issue(), Cancel);
    }

    private static AuthSession Started(Subject subject, string device)
        => AuthSession.Start(SessionId.Issue(), subject, subject.Value, AuthMethod.Local, device, At);
}
