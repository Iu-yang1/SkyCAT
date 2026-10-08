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

    [Theory]
    [InlineData("U SCOPE_STATE")]
    [InlineData("U SCOPE_STATE 2")]
    [InlineData("U SCOPE_MODE 0 4")]
    [InlineData("U SCOPE_HOLD 0 2")]
    [InlineData("U SCOPE_SPEED 0 3")]
    public void MalformedScopeCommandsReturnMinusOne(
      string command)
    {
      Assert.Equal(
        "RPRT -1",
        Make().Execute(command));
    }

    [Fact]
    public void ScopeControlRejectsNonIcomRadioWithoutOpeningPort()
    {
      Assert.Equal(
        "RPRT -6",
        Make().Execute(
          "U SCOPE_STATE 0"));
    }
  }
}
