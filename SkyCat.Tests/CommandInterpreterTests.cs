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
    [InlineData("U SCOPE_MODE MAIN BAD")]
    [InlineData("U SCOPE_SPAN SIDE 50000")]
    [InlineData("U SCOPE_EDGE MAIN nope")]
    [InlineData("U SCOPE_REF MAIN nope")]
    [InlineData("U SCOPE_SPEED MAIN TURBO")]
    [InlineData("U SCOPE_FIXED_EDGE 2 1 435000000")]
    public void MalformedScopeCommandsAreUnknown(
      string command)
    {
      Assert.Equal(
        "RPRT -11",
        Make().Execute(command));
    }

    [Theory]
    [InlineData("U SCOPE_MODE MAIN CENTER")]
    [InlineData("U SCOPE_SPAN MAIN 50000")]
    [InlineData("U SCOPE_EDGE MAIN 2")]
    [InlineData("U SCOPE_REF MAIN -3.5")]
    [InlineData("U SCOPE_SPEED MAIN FAST")]
    [InlineData("U SCOPE_FIXED_EDGE 2 1 435000000 436000000")]
    public void ValidScopeCommandsFailCleanlyWithoutOpenSerial(
      string command)
    {
      Assert.Equal(
        "RPRT -6",
        Make().Execute(command));
    }
  }
}
