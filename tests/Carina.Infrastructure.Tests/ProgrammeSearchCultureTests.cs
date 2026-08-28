using System.Globalization;

using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Programmes;

namespace Carina.Infrastructure.Tests;

public sealed class ProgrammeSearchCultureTests
{
    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    public static readonly string[] TheLanguagesThisIsMeasuredIn =
    [
        "tr-TR",
        "az-Latn-AZ",
        "ar-SA",
        "fa-IR",
        "de-DE",
        "th-TH",
        "sv-SE",
    ];

    public static TheoryData<string> EveryLanguage()
    {
        var carried = new TheoryData<string>();

        foreach (string named in TheLanguagesThisIsMeasuredIn)
        {
            carried.Add(named);
        }

        return carried;
    }

    [Theory]
    [MemberData(nameof(EveryLanguage))]
    public void TheFoldIsTheSameWhateverLanguageTheThreadIsSpeaking(string named)
        => Assert.Equal(Folded(CultureInfo.InvariantCulture), Folded(new CultureInfo(named)));

    [Theory]
    [MemberData(nameof(EveryLanguage))]
    public void AQueryStringIsReadTheSameWhateverLanguageTheThreadIsSpeaking(string named)
        => Assert.Equal(Read(CultureInfo.InvariantCulture), Read(new CultureInfo(named)));

    [Theory]
    [MemberData(nameof(EveryLanguage))]
    public void TheSameProgrammesComeBackWhateverLanguageTheThreadIsSpeaking(string named)
        => Assert.Equal(Found(CultureInfo.InvariantCulture), Found(new CultureInfo(named)));

    [Fact]
    public void TheLanguagesMeasuredHereAreOnesThatWouldTellIfTheCodeAskedTheThread()
    {
        Assert.NotEqual("i", Spoken(new CultureInfo("tr-TR"), () => "I".ToLower()));
        Assert.NotEqual("i", Spoken(new CultureInfo("az-Latn-AZ"), () => "I".ToLower()));
        Assert.False(Spoken(
            new CultureInfo("ar-SA"),
            () => int.TryParse("-1", NumberStyles.Integer, CultureInfo.CurrentCulture, out _)));
        Assert.False(Spoken(
            new CultureInfo("fa-IR"),
            () => int.TryParse("-1", NumberStyles.Integer, CultureInfo.CurrentCulture, out _)));
        Assert.Equal("1,5", Spoken(new CultureInfo("de-DE"), () => 1.5.ToString("0.#", CultureInfo.CurrentCulture)));
    }

    private static string Folded(CultureInfo speaking)
        => Spoken(
            speaking,
            () => string.Join(
                '|',
                ProgrammeSearchText.Folded("ＩＮＦＯ"),
                ProgrammeSearchText.Folded("I"),
                ProgrammeSearchText.Folded("İSTANBUL"),
                ProgrammeSearchText.Searchable("ＩＮＦＯ", "ｷﾞｮｳｻﾞ ①")));

    private static string Read(CultureInfo speaking)
        => Spoken(
            speaking,
            () => Spelt(ProgrammeSearchQuery.Read(
                "keyword=INFO&exclude=OTHER&fields=title&fields=description&genre=8&genre=-0"
                + "&type=isdbT&channel=4-1049&from=2026-08-18T00:00:00Z&to=2026-08-19T00:00:00Z"
                + "&sort=name&descending=true&page=-1&perPage=-7")));

    private static string Found(CultureInfo speaking)
        => Spoken(
            speaking,
            () => string.Join(
                '|',
                ProgrammeSearchMatching
                    .Search(Broadcast(), ProgrammeSearchQuery.Read("keyword=INFO&from=2026-08-18T00:00:00Z&to=2026-08-19T00:00:00Z")!, At)
                    .Items.Select(match => match.Name)));

    private static IReadOnlyList<ProgrammeMatch> Broadcast()
        => ProgrammeSearchMatching.Layered(
            [
                Held(1, "ＩＮＦＯの時間"),
                Held(2, "info の時間"),
                Held(3, "ｉｎｆｏ の続き"),
                Held(4, "天気予報"),
            ],
            []);

    private static Programme Held(int carried, string name)
        => Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(4), new ServiceId(1049), new EventId(carried)),
                new TransportStreamId(32_736),
                At,
                At.AddMinutes(30),
                name,
                string.Empty,
                false),
            At);

    private static string Spelt(ProgrammeSearch? search)
        => search is null
            ? "nothing"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{string.Join(',', search.Words)}|{string.Join(',', search.ExcludedWords)}|{string.Join(',', search.Fields)}|{string.Join(',', search.Genres)}|{search.System}|{string.Join(',', search.Channels)}|{search.From:O}|{search.To:O}|{search.Sort}|{search.Descending}|{search.Page}|{search.PerPage}");

    private static T Spoken<T>(CultureInfo speaking, Func<T> reading)
    {
        CultureInfo held = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = speaking;

        try
        {
            return reading();
        }
        finally
        {
            CultureInfo.CurrentCulture = held;
        }
    }
}
