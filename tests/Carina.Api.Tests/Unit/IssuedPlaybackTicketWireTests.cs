using System.Text.Json;

using Carina.Api.Common;
using Carina.Domain.Auth;

namespace Carina.Api.Tests.Unit;

public sealed class IssuedPlaybackTicketWireTests
{
    [Fact]
    public void WhatIsHandedBackCarriesTheTicketAndWhenItDiesAndNothingElse()
    {
        IssuedPlaybackTicket issued = new(
            Unguessable.Issue(),
            new DateTime(2026, 9, 1, 12, 0, 30, DateTimeKind.Utc));

        using JsonDocument written = JsonDocument.Parse(JsonSerializer.Serialize(issued, WireJson.Options));

        Assert.Equal(
            ["inTheClear", "lapsesAt"],
            written.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NothingAboutTheLedgerRidesAlongWithTheTicket()
    {
        Subject watcher = new("watcher");
        PlaybackTicket held = PlaybackTicket.Issue(
            watcher,
            PlaybackTarget.Recording("7"),
            new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
            out string inTheClear);
        IssuedPlaybackTicket issued = new(inTheClear, held.LapsesAt(PlaybackTicketPolicy.Default));

        string written = JsonSerializer.Serialize(issued, WireJson.Options);

        Assert.Contains(inTheClear, written, StringComparison.Ordinal);
        Assert.DoesNotContain(held.Digest, written, StringComparison.Ordinal);
        Assert.DoesNotContain(watcher.Value, written, StringComparison.Ordinal);
    }
}
