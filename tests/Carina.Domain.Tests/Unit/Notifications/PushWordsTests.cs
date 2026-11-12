using System.Text;

using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Unit.Notifications;

[Trait("Category", "Unit")]
public sealed class PushWordsTests
{
    private static readonly Func<string, int> Bytes = Encoding.UTF8.GetByteCount;

    [Fact(DisplayName = "D-11: words that fit are sent as they are, with none of the escapes a chat takes")]
    public void WordsThatFitAreSentAsTheyAre()
    {
        PushWords words = PushWords.Within("録画に失敗 *A* <b>", "1 行目 _x_ & ~y~\n2 行目 `z`", 3000, Bytes);

        Assert.Equal("録画に失敗 *A* <b>", words.Title);
        Assert.Equal("1 行目 _x_ & ~y~\n2 行目 `z`", words.Body);
    }

    [Fact(DisplayName = "D-11: a body too long for the room is cut at a whole character and ends in an ellipsis, and the words then fit")]
    public void ABodyTooLongIsCutAtAWholeCharacter()
    {
        string body = string.Concat(Enumerable.Repeat("あ", 2000));

        PushWords words = PushWords.Within("題", body, 300, Bytes);

        Assert.Equal("題", words.Title);
        Assert.Equal(string.Concat(Enumerable.Repeat("あ", 98)) + "…", words.Body);
        Assert.Equal(300, Bytes(words.Title) + Bytes(words.Body));
    }

    [Fact(DisplayName = "D-11: a cut never falls inside a character made of several code points")]
    public void ACutNeverFallsInsideACharacterOfSeveralCodePoints()
    {
        const string Family = "👨‍👩‍👧‍👦";
        string body = string.Concat(Enumerable.Repeat(Family, 20));

        PushWords words = PushWords.Within("t", body, 1 + 3 + (Bytes(Family) * 3) + 10, Bytes);

        Assert.Equal(Family + Family + Family + "…", words.Body);
    }

    [Fact(DisplayName = "D-11: a title too long takes a third of the room at most, and the body takes what is left")]
    public void ATitleTooLongTakesAThirdOfTheRoom()
    {
        string title = new('t', 256);
        string body = new('b', 2000);

        PushWords words = PushWords.Within(title, body, 300, Bytes);

        Assert.Equal(new string('t', 97) + "…", words.Title);
        Assert.Equal(new string('b', 197) + "…", words.Body);
        Assert.Equal(300, Bytes(words.Title) + Bytes(words.Body));
    }

    [Fact(DisplayName = "D-11: a short title is kept whole however long the body is")]
    public void AShortTitleIsKeptWhole()
    {
        PushWords words = PushWords.Within("A title", new string('b', 2000), 300, Bytes);

        Assert.Equal("A title", words.Title);
        Assert.Equal(300, Bytes(words.Title) + Bytes(words.Body));
    }

    [Fact(DisplayName = "D-11: the weight is that of the words as they are written, so a character that is written longer leaves room for fewer")]
    public void TheWeightIsThatOfTheWordsAsWritten()
    {
        Func<string, int> escaped = said => Bytes(said) + said.Count(character => character is '"');

        PushWords words = PushWords.Within("t", new string('"', 500), 104, escaped);

        Assert.Equal(new string('"', 50) + "…", words.Body);
        Assert.Equal(104, escaped(words.Title) + escaped(words.Body));
    }

    [Fact(DisplayName = "D-11: control characters and direction overrides are dropped, as for every notice")]
    public void ControlCharactersAreDropped()
    {
        PushWords words = PushWords.Within("A\u0007 title‮", "A\u0000 body\r\nnext", 3000, Bytes);

        Assert.Equal("A title", words.Title);
        Assert.Equal("A body\nnext", words.Body);
    }

    [Fact(DisplayName = "D-11: with no room at all, nothing is said rather than more than fits")]
    public void WithNoRoomNothingIsSaid()
    {
        PushWords words = PushWords.Within(new string('t', 100), new string('b', 100), 2, Bytes);

        Assert.Equal(string.Empty, words.Title);
        Assert.Equal(string.Empty, words.Body);
    }
}
