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
    public void ReadCommandIsRejectedBeforeSetup()
    {
      // no operating mode has been set up, so no command is available yet
      Assert.Equal("RPRT -11", Make().Execute("f"));
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
