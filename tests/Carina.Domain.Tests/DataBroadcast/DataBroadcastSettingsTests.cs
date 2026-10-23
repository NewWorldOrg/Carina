using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.DataBroadcast;

public sealed class DataBroadcastSettingsTests
{
    private static readonly DateTime Noon = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-BD-016: with no row held neither the postal code nor the prefecture is registered")]
    public void WithNoRowHeldNothingIsRegistered()
    {
        DataBroadcastSettingsStanding standing = DataBroadcastSettingsStanding.Over(null);

        Assert.Equal(DataBroadcastSettingsStanding.Unset, standing);
        Assert.Null(standing.PostalCode);
        Assert.Null(standing.Prefecture);
        Assert.Null(standing.ChangedAt);
    }

    [Fact(DisplayName = "BR-BD-016: a row held stands at the postal code and the prefecture it keeps, with when they changed")]
    public void ARowHeldStandsAtWhatItKeeps()
    {
        DataBroadcastSettingsStanding standing = DataBroadcastSettingsStanding.Over(
            DataBroadcastSettings.Rehydrate(DataBroadcastSettings.TheOnlyRow, "0000000", 13, Noon));

        Assert.Equal(("0000000", (int?)13, (DateTime?)Noon), (standing.PostalCode, standing.Prefecture, standing.ChangedAt));
    }

    [Theory(DisplayName = "BR-BD-016: a postal code is seven digits, and an empty one or none is not registered")]
    [InlineData("0000000", true, "0000000")]
    [InlineData("9990000", true, "9990000")]
    [InlineData("", true, null)]
    [InlineData(null, true, null)]
    [InlineData("000000", false, null)]
    [InlineData("00000000", false, null)]
    [InlineData("000-0000", false, null)]
    [InlineData(" 0000000", false, null)]
    [InlineData("000000a", false, null)]
    [InlineData("０００００００", false, null)]
    [InlineData("٠٠٠٠٠٠٠", false, null)]
    public void APostalCodeIsSevenDigits(string? asked, bool taken, string? registered)
    {
        Assert.Equal(taken, DataBroadcastSettingsStanding.TakesPostalCode(asked));

        if (taken)
        {
            Assert.Equal(registered, DataBroadcastSettingsStanding.PostalCodeOf(asked));
        }
    }

    [Theory(DisplayName = "BR-BD-016: a prefecture is its place in the prefecture designation counted from 0, from 0 to 49, or none")]
    [InlineData(0, true)]
    [InlineData(13, true)]
    [InlineData(49, true)]
    [InlineData(null, true)]
    [InlineData(-1, false)]
    [InlineData(50, false)]
    [InlineData(255, false)]
    public void APrefectureIsFromZeroToFortyNine(int? asked, bool taken)
        => Assert.Equal(taken, DataBroadcastSettingsStanding.TakesPrefecture(asked));

    [Theory(DisplayName = "BR-BD-016: a row keeps only a postal code of seven digits and a prefecture from 0 to 49, timed in UTC")]
    [InlineData("000000", 13)]
    [InlineData("", 13)]
    [InlineData("0000000", 50)]
    [InlineData("0000000", -1)]
    public void ARowKeepsOnlyWhatItWouldTake(string? postalCode, int? prefecture)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DataBroadcastSettings.Rehydrate(DataBroadcastSettings.TheOnlyRow, postalCode, prefecture, Noon));
        Assert.Throws<ArgumentException>(
            () => DataBroadcastSettings.Rehydrate(DataBroadcastSettings.TheOnlyRow, "0000000", 13, DateTime.SpecifyKind(Noon, DateTimeKind.Local)));
    }

    [Fact(DisplayName = "BR-BD-016: registering on no row makes the one row, and says when it changed")]
    public void RegisteringOnNoRowMakesTheOneRow()
    {
        DataBroadcastSettings? set = DataBroadcastSettingsStanding.Unset.Set("0000000", 13, Noon);

        Assert.NotNull(set);
        Assert.Equal(
            (DataBroadcastSettings.TheOnlyRow, "0000000", (int?)13, Noon),
            (set.Id, set.PostalCode, set.Prefecture, set.ChangedAt));
    }

    [Fact(DisplayName = "BR-BD-016: a postal code alone, or a prefecture alone, may be registered")]
    public void EitherMayBeRegisteredAlone()
    {
        DataBroadcastSettings? postal = DataBroadcastSettingsStanding.Unset.Set("0000000", null, Noon);
        DataBroadcastSettings? prefecture = DataBroadcastSettingsStanding.Unset.Set(null, 0, Noon);

        Assert.Equal(("0000000", (int?)null), (postal!.PostalCode, postal.Prefecture));
        Assert.Equal(((string?)null, (int?)0), (prefecture!.PostalCode, prefecture.Prefecture));
    }

    [Fact(DisplayName = "BR-BD-016: what was registered can be taken back, and the row says when")]
    public void WhatWasRegisteredCanBeTakenBack()
    {
        DataBroadcastSettingsStanding standing = DataBroadcastSettingsStanding.Over(
            DataBroadcastSettings.Rehydrate(DataBroadcastSettings.TheOnlyRow, "0000000", 13, Noon));
        DateTime later = Noon.AddHours(1);

        DataBroadcastSettings? cleared = standing.Set(null, null, later);

        Assert.Equal(((string?)null, (int?)null, later), (cleared!.PostalCode, cleared.Prefecture, cleared.ChangedAt));
    }

    [Fact(DisplayName = "BR-BD-016: a change that moves neither value writes no row")]
    public void AChangeThatMovesNothingWritesNoRow()
    {
        DataBroadcastSettingsStanding standing = DataBroadcastSettingsStanding.Over(
            DataBroadcastSettings.Rehydrate(DataBroadcastSettings.TheOnlyRow, "0000000", 13, Noon));

        Assert.Null(standing.Set("0000000", 13, Noon.AddHours(1)));
        Assert.Null(DataBroadcastSettingsStanding.Unset.Set(null, null, Noon));
    }

    [Fact(DisplayName = "BR-BD-016: a change moving one value keeps the other as it stands")]
    public void AChangeMovingOneValueKeepsTheOther()
    {
        DataBroadcastSettingsStanding standing = DataBroadcastSettingsStanding.Over(
            DataBroadcastSettings.Rehydrate(DataBroadcastSettings.TheOnlyRow, "0000000", 13, Noon));

        DataBroadcastSettings? moved = standing.Set("0000000", 48, Noon.AddHours(1));

        Assert.Equal(("0000000", (int?)48), (moved!.PostalCode, moved.Prefecture));
    }

    [Fact(DisplayName = "a change sets only what it would take")]
    public void AChangeSetsOnlyWhatItWouldTake()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DataBroadcastSettingsStanding.Unset.Set("000000", 13, Noon));
        Assert.Throws<ArgumentOutOfRangeException>(() => DataBroadcastSettingsStanding.Unset.Set("0000000", 50, Noon));
    }
}
