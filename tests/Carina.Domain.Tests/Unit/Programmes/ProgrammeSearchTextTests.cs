using System.Globalization;

using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Unit.Programmes;

[Trait("Category", "Unit")]
public sealed class ProgrammeSearchTextTests
{
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

    private static string Folded(CultureInfo speaking)
        => Spoken(
            speaking,
            () => string.Join(
                '|',
                ProgrammeSearchText.Folded("ＩＮＦＯ"),
                ProgrammeSearchText.Folded("I"),
                ProgrammeSearchText.Folded("İSTANBUL"),
                ProgrammeSearchText.Searchable("ＩＮＦＯ", "ｷﾞｮｳｻﾞ ①")));

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
