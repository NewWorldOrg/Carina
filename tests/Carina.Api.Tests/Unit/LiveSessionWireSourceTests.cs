using System.Threading.Channels;

using Carina.Api.Live;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Carina.Api.Tests.Unit;

public sealed class LiveSessionWireSourceTests
{
    private static readonly LiveSessionKey Asked = new(new NetworkId(32736), new ServiceId(1024), LiveProfile.Hd30);

    public static TheoryData<string, string, string> WhatIsNotAKey =>
        new()
        {
            { "32736", "1024", "hls" },
            { "32736", "1024", "720P30" },
            { "32736", "1024", "" },
            { "", "1024", "720p30" },
            { "32736", "", "720p30" },
            { "thirty", "1024", "720p30" },
            { "32736", "-1", "720p30" },
            { "65536", "1024", "720p30" },
            { "32736", "65536", "720p30" },
            { "0x7fe0", "1024", "720p30" },
            { " 32736", "1024", "720p30" },
        };

    [Fact]
    public void TheThreePartsOfTheQueryMakeTheKey()
    {
        Assert.Equal(Asked, LiveSessionWireSource.KeyOf(Query(("network", "32736"), ("service", "1024"), ("profile", "720p30"))));
    }

    [Theory]
    [MemberData(nameof(WhatIsNotAKey))]
    public void AQueryMissingOrMisspellingAPartIsNoKey(string network, string service, string profile)
    {
        Assert.Null(LiveSessionWireSource.KeyOf(Query(("network", network), ("service", service), ("profile", profile))));
    }

    [Fact]
    public void APartGivenTwiceIsNoKey()
    {
        QueryCollection query = new(new Dictionary<string, StringValues>
        {
            ["network"] = new StringValues(["32736", "32737"]),
            ["service"] = "1024",
            ["profile"] = "720p30",
        });

        Assert.Null(LiveSessionWireSource.KeyOf(query));
    }

    [Fact]
    public void AQueryWithNothingInItIsNoKey()
    {
        Assert.Null(LiveSessionWireSource.KeyOf(QueryCollection.Empty));
    }

    [Fact]
    public async Task AWireWithNoRequestBehindItJoinsNothing()
    {
        RememberedJoins sessions = new();
        LiveSessionWireSource source = new(new HttpContextAccessor(), sessions);

        Assert.Null(await source.JoinAsync(CancellationToken.None));
        Assert.Empty(sessions.Asked);
    }

    [Fact]
    public async Task TheKeyInTheRequestIsTheKeyThatIsJoined()
    {
        RememberedJoins sessions = new() { Answer = LiveJoin.Joined(new SeatedNowhere()) };
        LiveSessionWireSource source = new(Carrying(("network", "32736"), ("service", "1024"), ("profile", "720p30")), sessions);

        ILiveViewing? viewing = await source.JoinAsync(CancellationToken.None);

        Assert.NotNull(viewing);
        Assert.Equal([Asked], sessions.Asked);
    }

    [Fact]
    public async Task ARefusalReachesTheWireAsNothingToJoin()
    {
        RememberedJoins sessions = new() { Answer = LiveJoin.Refused(new TranscodeCeiling(4, 4)) };
        LiveSessionWireSource source = new(Carrying(("network", "32736"), ("service", "1024"), ("profile", "720p30")), sessions);

        Assert.Null(await source.JoinAsync(CancellationToken.None));
        Assert.Equal([Asked], sessions.Asked);
    }

    [Fact]
    public async Task ARequestNamingNoKeyIsNotPutToTheSessions()
    {
        RememberedJoins sessions = new();
        LiveSessionWireSource source = new(Carrying(("profile", "720p30")), sessions);

        Assert.Null(await source.JoinAsync(CancellationToken.None));
        Assert.Empty(sessions.Asked);
    }

    private static QueryCollection Query(params (string Name, string Value)[] parts)
        => new(parts.ToDictionary(part => part.Name, part => new StringValues(part.Value)));

    private static HttpContextAccessor Carrying(params (string Name, string Value)[] parts)
    {
        DefaultHttpContext context = new();

        context.Request.Query = Query(parts);

        return new HttpContextAccessor { HttpContext = context };
    }

    private sealed class RememberedJoins : ILiveSessionManager
    {
        public List<LiveSessionKey> Asked { get; } = [];

        public LiveJoin Answer { get; init; } = LiveJoin.Refused(LiveRefusal.NoTunerFree, "none");

        public Task<LiveJoin> JoinAsync(LiveSessionKey key, CancellationToken cancellationToken)
        {
            Asked.Add(key);

            return Task.FromResult(Answer);
        }
    }

    private sealed class SeatedNowhere : ILiveViewing
    {
        public ChannelReader<LiveFrame> Frames { get; } = Channel.CreateUnbounded<LiveFrame>().Reader;

        public LiveBacklog Backlog => LiveBacklog.Empty;

        public ILiveStartup? Startup => null;

        public ILiveEnding? Ending => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
