using skycatd;

namespace SkyCat.Tests
{
  public class WsjtXCommandInterpreterTests
  {
    [Theory]
    [InlineData("\\chk_vfo")]
    [InlineData("chk_vfo")]
    public void ChkVfoReportsNonVfoMode(string command)
    {
      var proxy = new WsjtXCommandInterpreter(_ => throw new InvalidOperationException());
      Assert.Equal("0", proxy.Execute(command));
    }

    [Theory]
    [InlineData("\\dump_state")]
    [InlineData("dump_state")]
    public void DumpStateIsHamlibProtocolV1(string command)
    {
      var proxy = new WsjtXCommandInterpreter(_ => throw new InvalidOperationException());
      string reply = proxy.Execute(command);

      Assert.StartsWith("1\n3081\n0\n", reply);
      Assert.Contains("ptt_type=0x5\n", reply);
      Assert.Contains("has_set_freq=1\n", reply);
      Assert.Contains("has_get_freq=1\n", reply);
      Assert.EndsWith("done", reply);
    }

    [Fact]
    public void FrequencyReadIsForwarded()
    {
      string? forwarded = null;
      var proxy = new WsjtXCommandInterpreter(command =>
      {
        forwarded = command;
        return "436795740";
      });

      Assert.Equal("436795740", proxy.Execute("f"));
      Assert.Equal("f", forwarded);
    }

    [Fact]
    public void ModeReadAddsRequiredPassbandRecord()
    {
      var proxy = new WsjtXCommandInterpreter(command =>
      {
        Assert.Equal("m", command);
        return "USB";
      });

      Assert.Equal("USB\n0", proxy.Execute("m"));
    }

    [Theory]
    [InlineData("ON", "1")]
    [InlineData("OFF", "0")]
    [InlineData("RPRT -11", "RPRT -11")]
    public void PttReadIsNormalizedForHamlib(string skyCatReply, string expected)
    {
      var proxy = new WsjtXCommandInterpreter(command =>
      {
        Assert.Equal("t", command);
        return skyCatReply;
      });

      Assert.Equal(expected, proxy.Execute("t"));
    }

    [Fact]
    public void PttWriteIsForwarded()
    {
      var forwarded = new List<string>();
      var proxy = new WsjtXCommandInterpreter(command =>
      {
        forwarded.Add(command);
        return "RPRT 0";
      });

      Assert.Equal("RPRT 0", proxy.Execute("T 1"));
      Assert.Equal("RPRT 0", proxy.Execute("T 0"));
      Assert.Equal(new[] { "T 1", "T 0" }, forwarded);
    }

    [Fact]
    public void DisconnectReleasesPttOnlyWhenProxyAssertedIt()
    {
      var forwarded = new List<string>();
      var proxy = new WsjtXCommandInterpreter(command =>
      {
        forwarded.Add(command);
        return "RPRT 0";
      });

      proxy.EnsurePttOff();
      Assert.Empty(forwarded);

      proxy.Execute("T 1");
      proxy.EnsurePttOff();

      Assert.Equal(new[] { "T 1", "T 0" }, forwarded);
    }

    [Fact]
    public void NearCurrentFrequencyWriteIsAcceptedWithoutChangingRadio()
    {
      var forwarded = new List<string>();
      var proxy = new WsjtXCommandInterpreter(command =>
      {
        forwarded.Add(command);
        return command == "f" ? "435604760" : "RPRT 0";
      });

      Assert.Equal("RPRT 0", proxy.Execute("F 435604755"));
      Assert.Equal(new[] { "f" }, forwarded);
    }

    [Fact]
    public void DistantFrequencyWriteIsBlockedAfterCheckingCurrentFrequency()
    {
      var forwarded = new List<string>();
      var proxy = new WsjtXCommandInterpreter(command =>
      {
        forwarded.Add(command);
        return command == "f" ? "435604760" : "RPRT 0";
      });

      Assert.Equal("RPRT -11", proxy.Execute("F 435614760"));
      Assert.Equal(new[] { "f" }, forwarded);
    }

    [Theory]
    [InlineData("I 145900000")]
    [InlineData("M USB 0")]
    [InlineData("X USB 0")]
    [InlineData("V VFOB")]
    [InlineData("S 1 VFOB")]
    [InlineData("C 670")]
    [InlineData("U SATMODE 1")]
    public void RadioStateWritesAreBlocked(string command)
    {
      bool forwarded = false;
      var proxy = new WsjtXCommandInterpreter(_ =>
      {
        forwarded = true;
        return "RPRT 0";
      });

      Assert.Equal("RPRT -11", proxy.Execute(command));
      Assert.False(forwarded);
    }

    [Fact]
    public void SplitAndVfoQueriesAreAnsweredWithoutTouchingRadio()
    {
      var proxy = new WsjtXCommandInterpreter(_ => throw new InvalidOperationException());

      Assert.Equal("VFOA", proxy.Execute("v"));
      Assert.Equal("0\nVFOB", proxy.Execute("s"));
    }
  }
}
