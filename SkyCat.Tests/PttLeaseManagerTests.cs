using skycatd;

namespace SkyCat.Tests;

public sealed class PttLeaseManagerTests
{
  [Fact]
  public void SharedCatAndWsjtXClientsCannotKeyOrUnkeyEachOther()
  {
    List<string> writes = new();
    string Radio(string command) { writes.Add(command); return "RPRT 0"; }
    var shared = new PttLeaseManager(Radio);
    var cat = new PttCommandSession(Radio, sharedLease: shared);
    var wsjtxLease = new PttCommandSession(Radio, sharedLease: shared);
    var wsjtx = new WsjtXCommandInterpreter(wsjtxLease.Execute);

    Assert.Equal("RPRT 0", cat.Execute("T 1"));
    Assert.Equal("RPRT -6", wsjtx.Execute("T 1"));
    Assert.Equal("RPRT 0", wsjtx.Execute("T 0"));
    Assert.Equal(new[] { "T 1" }, writes);

    cat.EnsurePttOff();
    Assert.Equal(new[] { "T 1", "T 0" }, writes);
    Assert.Equal("RPRT 0", wsjtx.Execute("T 3"));
    wsjtx.EnsurePttOff();
    wsjtxLease.EnsurePttOff();
    Assert.Equal(new[] { "T 1", "T 0", "T 1", "T 0" }, writes);
  }

  [Fact]
  public void AmbiguousPttOnTimeoutIsImmediatelyUnkeyed()
  {
    List<string> writes = new();
    string Radio(string command)
    {
      writes.Add(command);
      return command == "T 1" ? "RPRT -5" : "RPRT 0";
    }
    var shared = new PttLeaseManager(Radio);
    var cat = new PttCommandSession(Radio, sharedLease: shared);
    var next = new PttCommandSession(Radio, sharedLease: shared);

    Assert.Equal("RPRT -5", cat.Execute("T 1"));
    Assert.Equal(new[] { "T 1", "T 0" }, writes);
    Assert.Equal("RPRT -5", next.Execute("T 1"));
  }

  [Fact]
  public void FailedUnkeyBlocksOtherOwnersUntilRecovered()
  {
    List<string> writes = new();
    bool unkeyWorks = false;
    string Radio(string command)
    {
      writes.Add(command);
      return command == "T 1" ? "RPRT -5" :
        unkeyWorks ? "RPRT 0" : "RPRT -6";
    }
    var shared = new PttLeaseManager(Radio);
    var first = new PttCommandSession(Radio, sharedLease: shared);
    var second = new PttCommandSession(Radio, sharedLease: shared);
    Assert.Equal("RPRT -5", first.Execute("T 1"));
    Assert.Equal("RPRT -6", second.Execute("T 1"));
    first.EnsurePttOff();

    unkeyWorks = true;
    shared.RetryUncertainRelease();
    Assert.Equal("RPRT -5", second.Execute("T 1"));
    Assert.Equal(2, writes.Count(x => x == "T 1"));
  }

  [Fact]
  public void DefiniteNackDoesNotLeaveGhostLease()
  {
    string Radio(string command) =>
      command == "T 1" ? "RPRT -9" : "RPRT 0";
    var shared = new PttLeaseManager(Radio);
    var first = new PttCommandSession(Radio, sharedLease: shared);
    var second = new PttCommandSession(Radio, sharedLease: shared);
    Assert.Equal("RPRT -9", first.Execute("T 1"));
    Assert.Equal("RPRT -9", second.Execute("T 1"));
  }
}
