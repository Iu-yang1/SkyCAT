using skycatd;
using SkyCat;

namespace SkyCat.Tests;

public sealed class IcomRfGainTests
{
  [Theory]
  [InlineData(0, 0x00, 0x00)]
  [InlineData(1, 0x00, 0x01)]
  [InlineData(99, 0x00, 0x99)]
  [InlineData(100, 0x01, 0x00)]
  [InlineData(128, 0x01, 0x28)]
  [InlineData(255, 0x02, 0x55)]
  public void RfGainUsesTwoPackedDecimalBcdBytes(int gain, byte first, byte second)
  {
    Assert.Equal(new byte[] { first, second }, IcomRfGainCodec.Encode(gain));
    Assert.Equal(gain, IcomRfGainCodec.Decode(new byte[] { first, second }));
    Assert.Equal(new byte[] {
      0xFE, 0xFE, 0xA2, 0xE0, 0x14, 0x02, first, second, 0xFD
    }, IcomRfGainCodec.BuildWrite(gain));
  }

  [Fact]
  public void ReadQueryAndResponseAreStrictlyMatched()
  {
    Assert.Equal(new byte[] {
      0xFE, 0xFE, 0xA2, 0xE0, 0x14, 0x02, 0xFD
    }, IcomRfGainCodec.BuildRead());
    byte[] frame = {
      0xFE, 0xFE, 0xE0, 0xA2, 0x14, 0x02, 0x01, 0x28, 0xFD
    };
    Assert.True(IcomRfGainCodec.TryExtractReadReply(frame, out int gain));
    Assert.Equal(128, gain);
    frame[5] = 0x01; // radio AF gain must not be accepted as RF readback
    Assert.False(IcomRfGainCodec.TryExtractReadReply(frame, out _));
    frame[5] = 0x02;
    frame[4] = 0x27; // scope waveform must not be interpreted as RF gain
    Assert.False(IcomRfGainCodec.TryExtractReadReply(frame, out _));
  }

  [Theory]
  [InlineData(-1)]
  [InlineData(256)]
  [InlineData(1000)]
  public void OutOfRangeGainIsRejectedBeforeSerialWrite(int gain) =>
    Assert.Throws<ArgumentOutOfRangeException>(() => IcomRfGainCodec.Encode(gain));

  [Theory]
  [InlineData(0x02, 0x56)]
  [InlineData(0x03, 0x00)]
  [InlineData(0x0A, 0x00)]
  [InlineData(0x00, 0xFA)]
  public void InvalidBcdIsRejected(byte first, byte second) =>
    Assert.Throws<FormatException>(() =>
      IcomRfGainCodec.Decode(new byte[] { first, second }));

  [Fact]
  public void RfGainCommandsAreRecognizedButNotSentWithoutSerialConnection()
  {
    var rig = new CommandInterpreter(
      new Options { Model = "IC-9700", RigFile = "COM99" }, null);
    Assert.Equal("RPRT -6", rig.Execute("U RF_GAIN 128"));
    Assert.Equal("RPRT -6", rig.Execute("U RF_GAIN_READ"));
    Assert.Equal("RPRT -1", rig.Execute("U RF_GAIN 256"));
    Assert.Equal("RPRT -1", rig.Execute("U RF_GAIN -1"));

    var differentModel = new CommandInterpreter(
      new Options { Model = "TS-2000", RigFile = "COM99" }, null);
    Assert.Equal("RPRT -11", differentModel.Execute("U RF_GAIN 128"));
  }
}
