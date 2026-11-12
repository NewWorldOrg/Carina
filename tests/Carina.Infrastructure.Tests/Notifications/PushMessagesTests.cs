using System.Buffers.Text;
using System.Text.Json;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.TestSupport;

using Microsoft.AspNetCore.DataProtection;

namespace Carina.Infrastructure.Tests.Notifications;

public sealed class PushMessagesTests : IDisposable
{
    private static readonly DateTime At = new(2031, 4, 2, 3, 0, 0, DateTimeKind.Utc);

    private readonly HandTurnedClock clock = new(new DateTimeOffset(At));

    private readonly PushReceiver receiver = new();

    private readonly PushDesk desk;

    public PushMessagesTests()
    {
        desk = new PushDesk(new EphemeralDataProtectionProvider(), clock);
    }

    [Fact(DisplayName = "RFC 8292 section 2: a message to a browser is signed by the server's key for the origin of its push service, in the name of the origin of the screens")]
    public void AMessageIsSignedForThePushServiceInTheNameOfTheScreens()
    {
        Notice notice = Told(NoticeKind.RecordingCompleted);

        PushMessage message = desk.Messages.Telling(WebhookRig.Browser(receiver), notice, PushHandling.Usual);

        VapidReading token = VapidReading.Of(message.Authorization);
        Assert.True(token.Signed);
        Assert.Equal(desk.Key.PublicKey(), Base64Url.EncodeToString(token.Key));
        Assert.Equal("https://fcm.googleapis.com", token.Claims.GetProperty("aud").GetString());
        Assert.Equal("https://screens.example", token.Claims.GetProperty("sub").GetString());
        Assert.Equal(new DateTimeOffset(At).AddHours(12).ToUnixTimeSeconds(), token.Claims.GetProperty("exp").GetInt64());
        Assert.Equal(PushPayload.For(notice), message.Content);
        Assert.Equal(PushHandling.Usual, message.Handling);
    }

    [Fact(DisplayName = "D-11: the handling a message was given is the handling it carries")]
    public void TheHandlingAMessageWasGivenIsTheHandlingItCarries()
    {
        PushHandling handling = PushHandling.UntilTheEnd(At.AddMinutes(30), At);

        PushMessage message = desk.Messages.Telling(WebhookRig.Browser(receiver), Told(NoticeKind.ProgrammeStarting), handling);

        Assert.Equal(handling, message.Handling);
    }

    [Fact(DisplayName = "D-11: a trial is a message like any other, kept a day at normal urgency, that names no notice")]
    public void ATrialIsKeptADayAtNormalUrgencyAndNamesNoNotice()
    {
        PushMessage message = desk.Messages.Trying(WebhookRig.Browser(receiver), "テスト送信", "見本です。", "/settings/notifications");

        using JsonDocument read = JsonDocument.Parse(message.Content);
        Assert.Equal(PushHandling.Usual, message.Handling);
        Assert.True(VapidReading.Of(message.Authorization).Signed);
        Assert.Equal(JsonValueKind.Null, read.RootElement.GetProperty("id").ValueKind);
        Assert.Equal("/settings/notifications", read.RootElement.GetProperty("path").GetString());
    }

    [Fact(DisplayName = "RFC 8292 section 2.1: with no origin of the screens known, a message names no sender and is still signed")]
    public void WithNoOriginKnownAMessageNamesNoSender()
    {
        PushMessages messages = new(desk.Key, NoticeLinkBase.None, clock);

        PushMessage message = messages.Telling(WebhookRig.Browser(receiver), Told(NoticeKind.RecordingCompleted), PushHandling.Usual);

        VapidReading token = VapidReading.Of(message.Authorization);
        Assert.True(token.Signed);
        Assert.False(token.Claims.TryGetProperty("sub", out _));
    }

    [Fact(DisplayName = "D-11: messages can be signed once the server's key is made, and asking makes it")]
    public void MessagesCanBeSignedOnceTheKeyIsMade()
    {
        Assert.True(desk.Messages.CanSign());
        Assert.True(File.Exists(Path.Combine(desk.Directory.FullName, PushServerKey.FileName)));
    }

    [Fact(DisplayName = "D-11: with a key file that cannot be read no message can be signed, and the file is left as it is")]
    public void WithAKeyFileThatCannotBeReadNoMessageCanBeSigned()
    {
        desk.Directory.Create();
        string file = Path.Combine(desk.Directory.FullName, PushServerKey.FileName);
        File.WriteAllText(file, "not a key");

        Assert.False(desk.Messages.CanSign());
        Assert.Equal("not a key", File.ReadAllText(file));
    }

    public void Dispose()
    {
        desk.Dispose();
        receiver.Dispose();
    }

    private static Notice Told(NoticeKind kind)
        => Notice.Take(
            NoticeId.New(),
            kind,
            Guid.NewGuid(),
            At,
            new NoticeWords("A title", "A body", "https://screens.example/recordings"),
            At);
}
