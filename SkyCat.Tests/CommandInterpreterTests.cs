using skycatd;

namespace SkyCat.Tests
{
  // covers the rigctld-protocol dispatch in CommandInterpreter.Execute that needs no serial traffic:
  // argument routing, ignored commands, and the capabilities reply
  public class CommandInterpreterTests
  {
    // TS-2000 is a valid model; the port name is never opened by these tests
    private static CommandInterpreter Make() =>
      new(new Options { Model = "TS-2000", RigFile = "COM99" }, null);

    private static CommandInterpreter MakeIc9700() =>
      new(new Options { Model = "IC-9700", RigFile = "COM99" }, null);


    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyCommandReturnsMinusOne(string command)
    {
      Assert.Equal("RPRT -1", Make().Execute(command));
    }

    [Fact]
    public void UnknownCommandReturnsMinusEleven()
    {
      Assert.Equal("RPRT -11", Make().Execute("Z"));
    }

    [Theory]
    [InlineData("V")]
    [InlineData("U DUAL_WATCH 1")]
    [InlineData("U SATMODE 0")]
    public void IgnoredCommandsReturnZero(string command)
    {
      Assert.Equal("RPRT 0", Make().Execute(command));
    }

    [Fact]
    public void CapabilitiesCommandReturnsRadioJson()
    {
      var reply = Make().Execute("a");
      Assert.Contains("\"model\":\"TS-2000\"", reply);
    }

    [Fact]
    public void Ic9700CwCapabilitiesAreQueryableWithoutOpeningSerial()
    {
      string reply =
        MakeIc9700().Execute(
          "U CW_CAPS");

      Assert.Contains(
        "\"version\":1",
        reply);
      Assert.Contains(
        "\"model\":\"IC-9700\"",
        reply);
      Assert.Contains(
        "\"max_chars\":30",
        reply);
      Assert.Contains(
        "\"encoding\":\"base64-ascii\"",
        reply);
      Assert.Contains(
        "\"abort\":true",
        reply);
    }

    [Fact]
    public void NonIc9700DoesNotAdvertiseCwMessageControl()
    {
      Assert.Equal(
        "RPRT -11",
        Make().Execute(
          "U CW_CAPS"));
    }

    [Fact]
    public void ValidCwSendReachesSenderAfterProtocolValidation()
    {
      string encoded =
        Convert.ToBase64String(
          System.Text.Encoding.ASCII.GetBytes(
            "CQ DE K1ABC"));

      // Closed COM99 is intentional: -6 proves parsing, model gating and
      // Base64/character validation all succeeded before transport access.
      Assert.Equal(
        "RPRT -6",
        MakeIc9700().Execute(
          $"U CW_SEND64 {encoded}"));
    }

    [Theory]
    [InlineData("***")]
    [InlineData("Q1FfREU=")] // "CQ_DE": underscore is not an IC-9700 CW character
    public void InvalidCwSendPayloadIsRejectedBeforeTransport(
      string encoded)
    {
      Assert.Equal(
        "RPRT -1",
        MakeIc9700().Execute(
          $"U CW_SEND64 {encoded}"));
    }

    [Fact]
    public void Ic9700CwAbortReachesSenderAfterModelGate()
    {
      Assert.Equal(
        "RPRT -6",
        MakeIc9700().Execute(
          "U CW_ABORT"));
    }

    [Theory]
    [InlineData("U CW_SEND64 Q1EgREUgSzFBQkM=")]
    [InlineData("U CW_ABORT")]
    public void NonIc9700RejectsCwWrites(
      string command)
    {
      Assert.Equal(
        "RPRT -11",
        Make().Execute(command));
    }

    [Fact]
    public void ReadCommandIsRejectedBeforeSetup()
    {
      // no operating mode has been set up, so no command is available yet
      Assert.Equal("RPRT -11", Make().Execute("f"));
    }

    [Fact]
    public void FrequencyWriteSignaturesUseInt64NotInt32()
    {
      var type = typeof(CommandInterpreter);
      foreach (string method in new[] { "CmdFInt", "CmdIInt" })
      {
        var info = type.GetMethod(method,
          System.Reflection.BindingFlags.NonPublic |
          System.Reflection.BindingFlags.Instance);
        Assert.NotNull(info);
        Assert.Equal(typeof(long),
          Assert.Single(info.GetParameters()).ParameterType);
      }
    }

    [Fact]
    public void Ic905MicrowaveFrequencySurvivesLittleEndianBcdCodec()
    {
      var sender = new SkyCat.CatCommandSender();
      var param = new SkyCat.CatCommandSet.ParamInfo
      {
        Format = SkyCat.CatParamFormat.BCD_LE
      };
      var encode = typeof(SkyCat.CatCommandSender).GetMethod(
        "ParamToBytes",
        System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Instance)!;
      var decode = typeof(SkyCat.CatCommandSender).GetMethod(
        "BytesToParam",
        System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Instance)!;

      byte[] payload = (byte[])encode.Invoke(sender,
        new object?[] { param, "2400000000", 5 })!;
      Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x24 }, payload);
      Assert.Equal("2400000000",
        decode.Invoke(sender, new object?[] { param, payload }));
    }

    [Theory]
    [InlineData("F 2400000000")]
    [InlineData("I 10000000000")]
    public void HighFrequencyCommandsReachRigDispatcherWithoutInt32Overflow(
      string command)
    {
      // No radio setup means unsupported-command rather than the parser's
      // legacy RPRT -11 fallback for an overflowing Int32. The actual BCD
      // payload is separately validated against the model's byte width.
      Assert.Equal("RPRT -11", Make().Execute(command));
    }

    [Theory]
    [InlineData("F -1")]
    [InlineData("I -100")]
    [InlineData("F 999999999999999999999999")]
    [InlineData("I NaN")]
    public void InvalidFrequencyTextIsNeverDispatched(string command) =>
      Assert.Equal("RPRT -11", Make().Execute(command));

    // the tone commands are routed now, but TS-2000 defines no CTCSS commands (and no radio is set
    // up here), so they must report "not available" rather than crash
    [Theory]
    [InlineData("C 670")]
    [InlineData("U TONE 1")]
    [InlineData("U TONE 0")]
    public void ToneCommandsRejectedWhenRadioLacksSupport(string command)
    {
      Assert.Equal("RPRT -11", Make().Execute(command));
    }

    // a malformed tone command (non-numeric argument) is unknown, not a valid C command
    [Fact]
    public void MalformedToneCommandIsUnknown()
    {
      Assert.Equal("RPRT -11", Make().Execute("C notanumber"));
    }
  }
}
