using SkyCat;
using skycatd;

namespace SkyCat.Tests;

public sealed class IcomMeterCommandsTests
{
  [Theory]
  [InlineData(0x02, 0x01, 0x20, 120)]
  [InlineData(0x11, 0x02, 0x13, 213)]
  [InlineData(0x12, 0x00, 0x80, 80)]
  [InlineData(0x13, 0x00, 0x48, 48)]
  public void ReadbackMatchesCommandRadioAndBcd(
    byte kind, byte high, byte low, int expected)
  {
    byte[] frame = [0xFE, 0xFE, 0xE0, 0xA2, 0x15, kind, high, low, 0xFD];
    Assert.Equal(new byte[] { 0xFE, 0xFE, 0xA2, 0xE0, 0x15, kind, 0xFD },
      IcomMeterCivCodec.BuildRead(kind));
    Assert.True(IcomMeterCivCodec.TryDecode(frame, kind, out int raw));
    Assert.Equal(expected, raw);
    Assert.False(IcomMeterCivCodec.TryDecode(frame, 0x99, out _));
    frame[3] = 0xA4;
    Assert.False(IcomMeterCivCodec.TryDecode(frame, kind, out _));
  }

  [Fact]
  public void RejectsMalformedMeterAndAcceptsRadioBroadcastDestination()
  {
    byte[] frame = [0xFE, 0xFE, 0x00, 0xA2, 0x15, 0x02, 0x01, 0x20, 0xFD];
    Assert.True(IcomMeterCivCodec.TryDecode(frame, 0x02, out int raw));
    Assert.Equal(120, raw);
    frame[7] = 0xFF;
    Assert.False(IcomMeterCivCodec.TryDecode(frame, 0x02, out _));
    Assert.Throws<ArgumentOutOfRangeException>(
      () => IcomMeterCivCodec.BuildRead(0x10));
  }

  [Theory]
  [InlineData("S")]
  [InlineData("PWR")]
  [InlineData("ALC")]
  [InlineData("SWR")]
  public void UsesTheCurrentSkyCatSerialOwner(string field)
  {
    var interpreter = new CommandInterpreter(
      new Options { Model = "IC-9700", RigFile = "COM99" }, null);
    Assert.Equal("RPRT -6",
      interpreter.Execute("U METER_READ_FIELD " + field));
  }

  [Fact]
  public void UnsupportedRigAndInvalidFieldReturnErrors()
  {
    var icom = new CommandInterpreter(
      new Options { Model = "IC-9700", RigFile = "COM99" }, null);
    Assert.Equal("RPRT -1", icom.Execute("U METER_READ_FIELD UNKNOWN"));
    var other = new CommandInterpreter(
      new Options { Model = "TS-2000", RigFile = "COM99" }, null);
    Assert.Equal("RPRT -11", other.Execute("U METER_READ_FIELD S"));
  }
}
