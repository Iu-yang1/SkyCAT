using SkyCat;
using skycatd;

namespace SkyCat.Tests;

public sealed class IcomNetworkCommandsTests
{
  [Fact]
  public void IPv4DecodesFourBigEndianBcdOctets()
  {
    byte[] ip = { 0x01, 0x92, 0x01, 0x68, 0x00, 0x01, 0x01, 0x00 };
    Assert.Equal("192.168.1.100", IcomNetworkCivCodec.DecodeIPv4(ip));
  }

  [Fact]
  public void PortAndEnabledDecodeDocumentedBcdValues()
  {
    Assert.Equal(50001, IcomNetworkCivCodec.DecodePort(
      new byte[] { 0x05, 0x00, 0x01 }));
    Assert.Equal(65535, IcomNetworkCivCodec.DecodePort(
      new byte[] { 0x06, 0x55, 0x35 }));
    Assert.True(IcomNetworkCivCodec.DecodeEnabled(new byte[] { 0x01 }));
    Assert.False(IcomNetworkCivCodec.DecodeEnabled(new byte[] { 0x00 }));
  }

  [Fact]
  public void QueryReplyMustMatchReceiverCommandRegisterAndLength()
  {
    byte[] reply = {
      0xFE, 0xFE, 0xE0, 0xA2, 0x1A, 0x05, 0x01, 0x47,
      0x05, 0x00, 0x01, 0xFD
    };
    Assert.True(IcomNetworkCivCodec.TryExtractQueryReply(
      reply, 0x47, 3, out byte[] value));
    Assert.Equal(50001, IcomNetworkCivCodec.DecodePort(value));
    Assert.False(IcomNetworkCivCodec.TryExtractQueryReply(reply, 0x48, 3, out _));
    reply[3] = 0xA4;
    Assert.False(IcomNetworkCivCodec.TryExtractQueryReply(reply, 0x47, 3, out _));
    reply[3] = 0xA2;
    Assert.False(IcomNetworkCivCodec.TryExtractQueryReply(
      reply.AsSpan(0, reply.Length - 1), 0x47, 3, out _));
  }

  [Fact]
  public void BadBcdAndRangeCannotBecomeConfigurationValues()
  {
    Assert.Throws<FormatException>(() => IcomNetworkCivCodec.DecodeIPv4(
      new byte[] { 0x02, 0x56, 0, 0, 0, 0, 0, 0 }));
    Assert.Throws<FormatException>(() => IcomNetworkCivCodec.DecodePort(
      new byte[] { 0x06, 0x55, 0x36 }));
    Assert.Throws<FormatException>(() => IcomNetworkCivCodec.DecodePort(
      new byte[] { 0x05, 0x0A, 0x01 }));
    Assert.Throws<FormatException>(() => IcomNetworkCivCodec.DecodeEnabled(
      new byte[] { 0x02 }));
  }

  [Theory]
  [InlineData("IP")]
  [InlineData("CONTROL")]
  [InlineData("SERIAL")]
  [InlineData("AUDIO")]
  [InlineData("ENABLED")]
  public void QueryIsRecognizedAndRequiresActiveSerialTransport(string field)
  {
    var ci = new CommandInterpreter(
      new Options { Model = "IC-9700", RigFile = "COM99" }, null);
    Assert.Equal("RPRT -6",
      ci.Execute("U NETWORK_READ_FIELD " + field));
  }

  [Fact]
  public void UnknownFieldAndOtherRigRejected()
  {
    var ic9700 = new CommandInterpreter(
      new Options { Model = "IC-9700", RigFile = "COM99" }, null);
    Assert.Equal("RPRT -1", ic9700.Execute("U NETWORK_READ_FIELD PASSWORD"));
    var other = new CommandInterpreter(
      new Options { Model = "TS-2000", RigFile = "COM99" }, null);
    Assert.Equal("RPRT -11", other.Execute("U NETWORK_READ_FIELD IP"));
  }
}
