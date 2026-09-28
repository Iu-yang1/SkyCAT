using skycatd;

namespace SkyCat.Tests
{
  public class PttCommandSessionTests
  {
    [Fact]
    public void DisconnectReleasesOnlyPttAssertedByThisCatClient()
    {
      var forwarded = new List<string>();
      string Forward(string command)
      {
        forwarded.Add(command);
        return "RPRT 0";
      }

      var owner = new PttCommandSession(Forward);
      var bystander = new PttCommandSession(Forward);

      Assert.Equal("RPRT 0", owner.Execute("T 1"));

      bystander.EnsurePttOff();
      Assert.Equal(new[] { "T 1" }, forwarded);

      owner.EnsurePttOff();
      Assert.Equal(new[] { "T 1", "T 0" }, forwarded);
    }

    [Fact]
    public void FailedPttOnDoesNotAcquireOwnership()
    {
      var forwarded = new List<string>();
      string Forward(string command)
      {
        forwarded.Add(command);
        return command == "T 1" ? "RPRT -5" : "RPRT 0";
      }

      var session = new PttCommandSession(Forward);
      Assert.Equal("RPRT -5", session.Execute("T 1"));

      session.EnsurePttOff();
      Assert.Equal(new[] { "T 1" }, forwarded);
    }

    [Fact]
    public void IndependentWsjtXClientDoesNotReleaseAnotherClientsPtt()
    {
      var forwarded = new List<string>();
      string Forward(string command)
      {
        forwarded.Add(command);
        return "RPRT 0";
      }

      var owner = new WsjtXCommandInterpreter(Forward);
      var bystander = new WsjtXCommandInterpreter(Forward);

      Assert.Equal("RPRT 0", owner.Execute("T 1"));
      bystander.EnsurePttOff();
      Assert.Equal(new[] { "T 1" }, forwarded);

      owner.EnsurePttOff();
      Assert.Equal(new[] { "T 1", "T 0" }, forwarded);
    }
  }
}
