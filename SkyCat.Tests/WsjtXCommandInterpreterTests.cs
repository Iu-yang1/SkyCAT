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

    [Fact]
    public void DumpStateAdvertisesCompatibilityCapabilities()
    {
      var proxy = new WsjtXCommandInterpreter(_ => throw new InvalidOperationException());
      string reply = proxy.Execute("\\dump_state");

      Assert.StartsWith("1\n3081\n0\n", reply);
      Assert.Contains("ptt_type=0x5\n", reply);
      Assert.Contains("has_set_vfo=1\n", reply);
      Assert.Contains("has_set_freq=1\n", reply);
      Assert.Contains("has_get_freq=1\n", reply);
      Assert.Contains("rigctld_version=SkyCAT-WSJTX-2\n", reply);
      Assert.EndsWith("done", reply);
    }

    [Theory]
    [InlineData("f", "f", "436795740", "436795740")]
    [InlineData("\\get_freq", "f", "436795740", "436795740")]
    [InlineData("i", "i", "145850000", "145850000")]
    [InlineData("\\get_split_freq", "i", "145850000", "145850000")]
    public void FrequencyReadsAreForwarded(string input, string expectedForward, string radioReply, string expected)
    {
      var proxy = new WsjtXCommandInterpreter(command =>
      {
        Assert.Equal(expectedForward, command);
        return radioReply;
      });

      Assert.Equal(expected, proxy.Execute(input));
    }

    [Theory]
    [InlineData("m", "m")]
    [InlineData("\\get_mode", "m")]
    [InlineData("x", "x")]
    [InlineData("\\get_split_mode", "x")]
    public void ModeReadsAddPassbandRecord(string input, string expectedForward)
    {
      var proxy = new WsjtXCommandInterpreter(command =>
      {
        Assert.Equal(expectedForward, command);
        return "USB";
      });

      Assert.Equal("USB\n0", proxy.Execute(input));
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

    [Theory]
    [InlineData("T 0", "T 0")]
    [InlineData("T 1", "T 1")]
    [InlineData("T 2", "T 1")]
    [InlineData("T 3", "T 1")]
    [InlineData("\\set_ptt 3", "T 1")]
    public void PttWritesAreTheOnlyWritesForwarded(string input, string expectedForward)
    {
      var forwarded = new List<string>();
      var proxy = new WsjtXCommandInterpreter(command =>
      {
        forwarded.Add(command);
        return "RPRT 0";
      });

      Assert.Equal("RPRT 0", proxy.Execute(input));
      Assert.Equal(new[] { expectedForward }, forwarded);
    }

    [Theory]
    [InlineData("T -1")]
    [InlineData("T 4")]
    [InlineData("T bad")]
    [InlineData("\\set_ptt 99")]
    public void InvalidPttIsRejected(string input)
    {
      var proxy = new WsjtXCommandInterpreter(_ => throw new InvalidOperationException());
      Assert.Equal("RPRT -1", proxy.Execute(input));
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

      proxy.Execute("T 3");
      proxy.EnsurePttOff();

      Assert.Equal(new[] { "T 1", "T 0" }, forwarded);
    }

    [Theory]
    [InlineData("F 435605255.000000")]
    [InlineData("F 435605255")]
    [InlineData("I 145900000.000000")]
    [InlineData("M USB 0")]
    [InlineData("X LSB 2400")]
    [InlineData("V VFOB")]
    [InlineData("S 1 VFOB")]
    [InlineData("C 670")]
    [InlineData("U SATMODE 1")]
    [InlineData("J 0")]
    [InlineData("Z 0")]
    [InlineData("L RFPOWER 0.5")]
    [InlineData("\\set_freq 435605255.000000")]
    [InlineData("\\set_mode USB 0")]
    [InlineData("\\set_vfo VFOB")]
    [InlineData("\\set_split_vfo 1 VFOB")]
    [InlineData("\\set_split_freq 145900000.000000")]
    [InlineData("\\set_split_mode USB 0")]
    public void NonPttWritesAreAcknowledgedButNeverForwarded(string command)
    {
      bool forwarded = false;
      var proxy = new WsjtXCommandInterpreter(_ =>
      {
        forwarded = true;
        return "RPRT 0";
      });

      Assert.Equal("RPRT 0", proxy.Execute(command));
      Assert.False(forwarded);
    }

    [Fact]
    public void SplitVfoAndInfoQueriesDoNotTouchRadio()
    {
      var proxy = new WsjtXCommandInterpreter(_ => throw new InvalidOperationException());

      Assert.Equal("VFOA", proxy.Execute("v"));
      Assert.Equal("0\nVFOB", proxy.Execute("s"));
      Assert.Equal("SkyCAT WSJT-X compatibility proxy", proxy.Execute("\\get_info"));
    }

    [Fact]
    public void WsjtXTypicalSessionCompletesWithoutUnsupportedErrors()
    {
      var forwarded = new List<string>();
      var proxy = new WsjtXCommandInterpreter(command =>
      {
        forwarded.Add(command);
        return command switch
        {
          "f" => "435605255",
          "m" => "USB",
          "t" => "OFF",
          "T 1" => "RPRT 0",
          "T 0" => "RPRT 0",
          _ => "RPRT -11"
        };
      });

      Assert.Equal("0", proxy.Execute("\\chk_vfo"));
      Assert.StartsWith("1\n", proxy.Execute("\\dump_state"));
      Assert.Equal("435605255", proxy.Execute("f"));
      Assert.Equal("RPRT 0", proxy.Execute("F 435605255.000000"));
      Assert.Equal("USB\n0", proxy.Execute("m"));
      Assert.Equal("RPRT 0", proxy.Execute("M USB 0"));
      Assert.Equal("0", proxy.Execute("t"));
      Assert.Equal("RPRT 0", proxy.Execute("T 3"));
      Assert.Equal("RPRT 0", proxy.Execute("T 0"));

      Assert.Equal(new[] { "f", "m", "t", "T 1", "T 0" }, forwarded);
    }

    [Fact]
    public void UnknownQueryStillReportsUnavailable()
    {
      var proxy = new WsjtXCommandInterpreter(_ => throw new InvalidOperationException());
      Assert.Equal("RPRT -11", proxy.Execute("\\get_dcd"));
    }
  }
}
