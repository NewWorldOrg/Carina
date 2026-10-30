using System.Net;

using Carina.Driver.CardLending;
using Carina.Driver.Configuration;
using Carina.Driver.Descrambling;

namespace Carina.Driver.Tests;

public sealed class CardLendingConfigurationTests
{
    private const string Without = """
        {
          "outputRoots": [{ "name": "primary", "path": "/srv/recordings" }],
          "tuner": { "backend": "usb" },
          "devices": [
            { "id": "1-1", "kind": "terrestrial", "devicePath": "/dev/bus/usb/001/002" }
          ]
        }
        """;

    private static string With(string cardLending) =>
        Without.Replace("\"tuner\":", $"\"cardLending\": {cardLending},\n  \"tuner\":", StringComparison.Ordinal);

    private static string Lending(string extra = "", string address = "0.0.0.0") =>
        With($$"""{ "lend": { "address": "{{address}}", "hostNames": ["lender.example"], "keyFile": "/etc/carina/lending.key"{{extra}} } }""");

    private static string Borrowing(string extra = "", string address = "192.168.77.8") =>
        With($$"""{ "borrow": { "address": "{{address}}", "hostName": "lender.example", "keyFile": "/etc/carina/lending.key"{{extra}} } }""");

    private static IReadOnlyList<string> Problems(string json) => DriverConfigurationReader.Read(json).Problems;

    private static DriverConfiguration Read(string json)
    {
        DriverConfigurationResult result = DriverConfigurationReader.Read(json);

        Assert.True(result.TryGetConfiguration(out DriverConfiguration configuration, out IReadOnlyList<string> problems), string.Join(" ", problems));

        return configuration;
    }

    [Fact]
    public void ADriverLendsNothingUntilItIsToldTo()
    {
        Assert.Null(Read(Without).CardLending);
    }

    [Fact]
    public void ALenderTakesTheDefaultsItIsNotGiven()
    {
        LendSettings lend = Read(Lending()).CardLending!.Lend!;

        Assert.Equal(CardLendingSettings.DefaultPort, lend.Port);
        Assert.Equal(CardLendingSettings.DefaultHopLimit, lend.HopLimit);
        Assert.Equal(CardLendingSettings.DefaultLeases, lend.Leases);
        Assert.Equal(CardLendingSettings.DefaultRoundTripMilliseconds, lend.RoundTripMilliseconds);
        Assert.Equal(5, CardLendingSettings.DefaultRoundTripMilliseconds);
        Assert.Equal(["lender.example"], lend.HostNames);
    }

    [Fact]
    public void ABorrowerTakesTheDefaultsItIsNotGiven()
    {
        BorrowSettings borrow = Read(Borrowing()).CardLending!.Borrow!;

        Assert.Equal(CardLendingSettings.DefaultPort, borrow.Port);
        Assert.Equal(CardLendingSettings.DefaultHopLimit, borrow.HopLimit);
        Assert.Equal("lender.example", borrow.HostName);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.77.8")]
    [InlineData("fd12:3456:789a::1")]
    public void ALenderListensOnEveryAddressOrOneInsideTheHouse(string address)
    {
        Assert.Empty(Problems(Lending(address: address)));
    }

    [Theory]
    [InlineData("203.0.113.5")]
    [InlineData("100.64.0.1")]
    [InlineData("2001:db8::1")]
    [InlineData("lender.example")]
    public void ALenderDoesNotListenOnAnAddressThatCanBeOutsideTheHouse(string address)
    {
        Assert.Contains(Problems(Lending(address: address)), problem => problem.StartsWith("cardLending.lend.address:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("192.0.2.10")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("lender.example")]
    public void ABorrowerAsksOnlyAnAddressInsideTheHouse(string address)
    {
        Assert.Contains(Problems(Borrowing(address: address)), problem => problem.StartsWith("cardLending.borrow.address:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"hopLimit\": 0", "cardLending.lend.hopLimit:")]
    [InlineData("\"hopLimit\": 9", "cardLending.lend.hopLimit:")]
    [InlineData("\"port\": 80", "cardLending.lend.port:")]
    [InlineData("\"port\": 65536", "cardLending.lend.port:")]
    [InlineData("\"leases\": 0", "cardLending.lend.leases:")]
    [InlineData("\"leases\": 17", "cardLending.lend.leases:")]
    [InlineData("\"roundTripMilliseconds\": 0", "cardLending.lend.roundTripMilliseconds:")]
    [InlineData("\"roundTripMilliseconds\": 31", "cardLending.lend.roundTripMilliseconds:")]
    [InlineData("\"addres\": \"0.0.0.0\"", "cardLending.lend.addres:")]
    public void ALendingSettingOutsideItsShapeNamesItself(string extra, string expected)
    {
        Assert.Contains(Problems(Lending($", {extra}")), problem => problem.StartsWith(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"hostNames\": [], \"keyFile\": \"/etc/carina/lending.key\"", "cardLending.lend.hostNames:")]
    [InlineData("\"hostNames\": [\"not a name\"], \"keyFile\": \"/etc/carina/lending.key\"", "cardLending.lend.hostNames[0]:")]
    [InlineData("\"hostNames\": [\"lender.example\"], \"keyFile\": \"lending.key\"", "cardLending.lend.keyFile:")]
    [InlineData("\"hostNames\": [\"lender.example\"]", "cardLending.lend.keyFile:")]
    public void ALenderNeedsItsNamesAndItsKey(string rest, string expected)
    {
        string json = With($$"""{ "lend": { "address": "0.0.0.0", {{rest}} } }""");

        Assert.Contains(Problems(json), problem => problem.StartsWith(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"hopLimit\": 0", "cardLending.borrow.hopLimit:")]
    [InlineData("\"port\": 1023", "cardLending.borrow.port:")]
    [InlineData("\"lender\": \"192.168.77.8\"", "cardLending.borrow.lender:")]
    public void ABorrowingSettingOutsideItsShapeNamesItself(string extra, string expected)
    {
        Assert.Contains(Problems(Borrowing($", {extra}")), problem => problem.StartsWith(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void ABorrowerNamesTheHostItAsks()
    {
        string json = With("""{ "borrow": { "address": "192.168.77.8", "hostName": "", "keyFile": "/etc/carina/lending.key" } }""");

        Assert.Contains(Problems(json), problem => problem.StartsWith("cardLending.borrow.hostName:", StringComparison.Ordinal));
    }

    [Fact]
    public void ADriverLendsOrBorrowsButNotBoth()
    {
        string json = With("""
            {
              "lend": { "address": "0.0.0.0", "hostNames": ["lender.example"], "keyFile": "/etc/carina/lending.key" },
              "borrow": { "address": "192.168.77.8", "hostName": "lender.example", "keyFile": "/etc/carina/lending.key" }
            }
            """);

        Assert.Contains(Problems(json), problem => problem.StartsWith("cardLending:", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnknownCardLendingSettingIsNamed()
    {
        Assert.Contains(
            Problems(With("""{ "lent": {} }""")),
            problem => problem.StartsWith("cardLending.lent:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void AKeyFileOthersCanReadStopsTheDriver()
    {
        string root = Directory.CreateTempSubdirectory("carina-lending-").FullName;

        try
        {
            string key = Path.Combine(root, "lending.key");
            File.WriteAllText(key, new string('k', LendingKey.ShortestText));
            File.SetUnixFileMode(key, UnixFileMode.UserRead | UnixFileMode.OtherRead);
            DriverConfiguration configuration = new(
                SocketPath: Path.Combine(root, "driver.sock"),
                OutputRoots: [new OutputRootSettings("primary", root)],
                Tuner: new TunerSettings(TunerBackend.Fake),
                CardLending: new CardLendingSettings(Lend: new LendSettings("127.0.0.1", HostNames: ["lender.example"], KeyFile: key))
            );

            DriverConfigurationResult result = DriverConfigurationReader.CheckTheFilesystem(configuration);

            Assert.Contains(result.Problems, problem => problem.StartsWith("cardLending.lend.keyFile:", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LendingSurvivesTheDriverWritingItsLedgerBack()
    {
        DriverConfiguration written = Read(Lending(", \"roundTripMilliseconds\": 20, \"leases\": 2"));

        LendSettings lend = Read(DriverConfigurationWriter.Serialize(written)).CardLending!.Lend!;

        Assert.Equal("0.0.0.0", lend.Address);
        Assert.Equal(["lender.example"], lend.HostNames);
        Assert.Equal(20, lend.RoundTripMilliseconds);
        Assert.Equal(2, lend.Leases);
        Assert.Equal("/etc/carina/lending.key", lend.KeyFile);
    }

    [Fact]
    public void ADriverThatNeitherLendsNorBorrowsWritesItsLedgerBackReadably()
    {
        Assert.Null(Read(DriverConfigurationWriter.Serialize(Read(Without))).CardLending);
    }

    [Fact]
    public void ADriverThatBorrowsAsksTheLenderForItsKeys()
    {
        DriverConfiguration configuration = Read(Borrowing());
        CardLoan loan = new(new IPEndPoint(IPAddress.Parse("192.168.77.8"), 40772), "lender.example", 2, LendingKey.FromText(new string('k', 40)));

        Assert.IsType<BorrowedCards>(Descramblers.For(configuration, loan, logger: null));
    }

    [Fact]
    public void AUsbDriverThatDoesNotBorrowUnscramblesNothing()
    {
        Assert.Same(NoDescrambling.Instance, Descramblers.For(Read(Without), loan: null, logger: null));
    }
}
