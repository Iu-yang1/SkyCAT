using SkyCat;
using skycatd;

namespace SkyCat.Tests;

public sealed class IcomScopeCommandsTests
{
  [Theory]
  [InlineData(2500L)]
  [InlineData(25000L)]
  [InlineData(500000L)]
  public void ScopeSpanRoundTripsOverFiveByteLittleEndianBcd(long span)
  {
    Assert.Equal(span, IcomScopeCivCodec.DecodeSpan(IcomScopeCivCodec.EncodeSpan(span)));
  }

  [Fact]
  public void ScopeSpan25000HasDocumentedIcomBcdBytes()
  {
    Assert.Equal(
      new byte[] { 0x00, 0x50, 0x02, 0x00, 0x00 },
      IcomScopeCivCodec.EncodeSpan(25000));
    var cmd = IcomScopeCommands.BuildWrite(
      new[] { "U", "SCOPE_SPAN", "MAIN", "25000" });
    Assert.Equal((byte)0x15, cmd.Subcommand);
    Assert.Equal(
      new byte[] { 0x00, 0x00, 0x50, 0x02, 0x00, 0x00 },
      cmd.Payload);
  }

  [Theory]
  [InlineData(-20.0)]
  [InlineData(-12.5)]
  [InlineData(0.0)]
  [InlineData(15.0)]
  [InlineData(20.0)]
  public void ReferenceLevelRoundTripsUsingIcomSignedBcd(double db)
  {
    Assert.Equal(db, IcomScopeCivCodec.DecodeReference(IcomScopeCivCodec.EncodeReference(db)));
  }

  [Fact]
  public void NegativeTwelvePointFiveReferenceUsesSignAtEnd()
  {
    var cmd = IcomScopeCommands.BuildWrite(
      new[] { "U", "SCOPE_REF", "SUB", "-12.5" });
    Assert.Equal((byte)0x19, cmd.Subcommand);
    Assert.Equal(new byte[] { 0x01, 0x12, 0x50, 0x01 }, cmd.Payload);
  }

  [Theory]
  [InlineData("U SCOPE_SELECT MAIN", 0x12)]
  [InlineData("U SCOPE_MODE SUB SCROLL-F", 0x14)]
  [InlineData("U SCOPE_EDGE MAIN 3", 0x16)]
  [InlineData("U SCOPE_SPEED MAIN SLOW", 0x1A)]
  [InlineData("U SCOPE_VBW SUB NARROW", 0x1D)]
  [InlineData("U SCOPE_TX 1", 0x1B)]
  [InlineData("U SCOPE_CENTER_TYPE ABS", 0x1C)]
  [InlineData("U SCOPE_MARKER CARRIER", 0x20)]
  [InlineData("U SCOPE_FIXED_EDGE 2 4 435000000 436000000", 0x1E)]
  public void AllSkyRoofControlNamesMapToExpectedCivCommands(string command, byte subCommand)
  {
    var tx = IcomScopeCommands.BuildWrite(command.Split(' '));
    Assert.Equal(subCommand, tx.Subcommand);
  }

  [Theory]
  [InlineData("U SCOPE_SPAN MAIN 7000")]
  [InlineData("U SCOPE_MODE MAIN BAD")]
  [InlineData("U SCOPE_REF MAIN 30.0")]
  [InlineData("U SCOPE_REF MAIN 4.3")]
  [InlineData("U SCOPE_EDGE MAIN 5")]
  [InlineData("U SCOPE_SELECT THIRD")]
  [InlineData("U SCOPE_VBW MAIN MEDIUM")]
  [InlineData("U SCOPE_FIXED_EDGE 2 1 144000000 148000000")]
  [InlineData("U SCOPE_FIXED_EDGE 2 1 436000000 435000000")]
  public void InvalidCommandsCannotEmitRadioWrites(string command)
  {
    Assert.ThrowsAny<ArgumentException>(
      () => IcomScopeCommands.BuildWrite(command.Split(' ')));
  }

  [Fact]
  public void FrequencyCodecRejectsMalformedNibbles()
  {
    Assert.Throws<FormatException>(() => IcomScopeCivCodec.DecodeFrequency(
      new byte[] { 0, 0, 0xFA, 0, 0 }));
  }

  [Fact]
  public void ScopeQueryMatcherSkipsUnsolicitedWaterfallAndWrongReceiver()
  {
    byte[] waveform = new byte[] {
      0xFE, 0xFE, 0xE0, 0xA2, 0x27, 0x00, 0x00, 0x01, 0x01, 0xFD
    };
    byte[] wrongScope = new byte[] {
      0xFE, 0xFE, 0xE0, 0xA2, 0x27, 0x1A, 0x01, 0x02, 0xFD
    };
    byte[] selected = new byte[] {
      0xFE, 0xFE, 0xE0, 0xA2, 0x27, 0x1A, 0x00, 0x02, 0xFD
    };
    Assert.False(IcomScopeCivCodec.TryExtractQueryReply(
      waveform, 0x1A, new byte[] { 0x00 }, 1, out _));
    Assert.False(IcomScopeCivCodec.TryExtractQueryReply(
      wrongScope, 0x1A, new byte[] { 0x00 }, 1, out _));
    Assert.True(IcomScopeCivCodec.TryExtractQueryReply(
      selected, 0x1A, new byte[] { 0x00 }, 1, out byte[] reply));
    Assert.Equal(new byte[] { 0x02 }, reply);
  }

  [Fact]
  public void ScopeQueryMatcherRejectsStrayControllerAddressAndTruncatedReply()
  {
    byte[] frame = new byte[] {
      0xFE, 0xFE, 0xE0, 0xA2, 0x27, 0x15,
      0x00, 0x00, 0x50, 0x02, 0x00, 0x00, 0xFD
    };
    Assert.True(IcomScopeCivCodec.TryExtractQueryReply(
      frame, 0x15, new byte[] { 0x00 }, 5, out byte[] value));
    Assert.Equal(25000, IcomScopeCivCodec.DecodeSpan(value));

    frame[3] = 0xA4;
    Assert.False(IcomScopeCivCodec.TryExtractQueryReply(
      frame, 0x15, new byte[] { 0x00 }, 5, out _));
    frame[3] = 0xA2;
    Assert.False(IcomScopeCivCodec.TryExtractQueryReply(
      frame.AsSpan(0, frame.Length - 1), 0x15,
      new byte[] { 0x00 }, 5, out _));
  }

  [Fact]
  public void ValidScopeCommandsNowDispatchBeyondOldUnsupportedReturn()
  {
    var interpreter = new CommandInterpreter(
      new Options { Model = "IC-9700", RigFile = "COM99" }, null);
    // No serial connection is opened by the unit test. The command is
    // recognized, and the backend reports a disconnected transport.
    Assert.Equal("RPRT -6", interpreter.Execute("U SCOPE_SELECT MAIN"));
    Assert.Equal("RPRT -6", interpreter.Execute("U SCOPE_READ"));
    Assert.Equal("RPRT -6", interpreter.Execute("U SCOPE_READ_EDGE 2 1"));
  }

  [Fact]
  public void OtherRadioCannotReceiveIcomScopeCommands()
  {
    var interpreter = new CommandInterpreter(
      new Options { Model = "TS-2000", RigFile = "COM99" }, null);
    Assert.Equal("RPRT -11", interpreter.Execute("U SCOPE_SELECT MAIN"));
    Assert.Equal("RPRT -11", interpreter.Execute("U SCOPE_READ"));
  }
}
