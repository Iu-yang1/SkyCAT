using skycatd;

namespace SkyCat.Tests;

public sealed class IcomSwitchCommandsTests
{
  [Theory]
  [InlineData("DATA_OFF", "1A050115", 1, true)]
  [InlineData("DATA_MOD", "1A050116", 1, true)]
  [InlineData("USB_OUTPUT", "1A050105", 1, true)]
  [InlineData("COMP", "1644", 1, true)]
  [InlineData("COMP_LEVEL", "140E", 2, true)]
  [InlineData("KEY_SPEED", "140C", 2, true)]
  [InlineData("RF_POWER", "140A", 2, true)]
  [InlineData("SAT_MODE", "165A", 1, false)]
  public void MatchesOfficialIc9700Selector(string key, string hex,
    int len, bool writable)
  {
    var actual = IcomSwitchCommands.Describe(key);
    Assert.Equal(hex, Convert.ToHexString(actual.Selector));
    Assert.Equal(len, actual.DataBytes);
    Assert.Equal(writable, actual.Writable);
  }

  [Theory]
  [InlineData("DATA_OFF", "05")]
  [InlineData("DATA_MOD", "03")]
  [InlineData("USB_OUTPUT", "01")]
  [InlineData("COMP", "01")]
  [InlineData("RF_POWER", "0255")]
  [InlineData("COMP_LEVEL", "0128")]
  [InlineData("KEY_SPEED", "0000")]
  public void ValidValuesRoundTrip(string key, string hex) =>
    Assert.Equal(hex, Convert.ToHexString(
      IcomSwitchCommands.ParseAndValidateValue(key, hex)));

  [Theory]
  [InlineData("DATA_OFF", "06")]
  [InlineData("USB_OUTPUT", "02")]
  [InlineData("COMP", "FF")]
  [InlineData("RF_POWER", "0256")]
  [InlineData("COMP_LEVEL", "03A0")]
  [InlineData("KEY_SPEED", "000")]
  [InlineData("SAT_MODE", "01")]
  public void RejectsOutOfRangeAndReadonlyWrites(string key, string hex) =>
    Assert.Throws<ArgumentException>(() =>
      IcomSwitchCommands.ParseAndValidateValue(key, hex));

  [Fact]
  public void RejectsAllOtherControlsBeforeReachingRadio()
  {
    var interpreter = new CommandInterpreter(new Options {
      Model = "IC-9700", RigFile = "COM99"
    }, null);
    var sender = interpreter.CommandSender;
    foreach (string request in new[] {
      "T 1", "F 145900000", "SET SAT_MODE 01",
      "GET TX_FREQ", "SET RF_GAIN 0255", "SET DATA_OFF FF",
      "SET RF_POWER 9999", "U SCOPE 1", "SET VFO D1",
      "GET", "SET COMP_LEVEL 0256", "SET USB_OUTPUT 05"
    })
      Assert.Equal("ERR INVALID", IcomSwitchCommands.Execute(sender, request));

    Assert.Equal("ERR DISCONNECTED",
      IcomSwitchCommands.Execute(sender, "GET COMP"));
    Assert.Equal("ERR DISCONNECTED",
      IcomSwitchCommands.Execute(sender, "SET DATA_OFF 05"));
  }

  [Fact]
  public void ExplicitPortCannotOverlapExistingServices()
  {
    var opts = new Options {
      Model = "IC-9700", RigFile = "COM99",
      Port = 4532, WsjtXPort = 4534, ScopePort = 4535,
      SwitchPort = 4536
    };
    Assert.True(opts.Validate());
    opts.SwitchPort = opts.ScopePort;
    Assert.False(opts.Validate());
    opts.SwitchPort = opts.Port;
    Assert.False(opts.Validate());
    opts.DisableSwitchPort = true;
    Assert.True(opts.Validate());
  }
}
