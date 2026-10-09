using skycatd;

namespace SkyCat.Tests
{
  public sealed class CwMessageLeaseManagerTests
  {
    private const string SendA =
      "U CW_SEND64 Q1EgREUgSzFBQkM=";
    private const string SendB =
      "U CW_SEND64 Q1EgREUgVzlYWVo=";
    private const string Abort =
      "U CW_ABORT";

    [Fact]
    public void ClientsCannotSendOrAbortEachOthersCw()
    {
      var writes =
        new List<string>();

      string Radio(string command)
      {
        writes.Add(command);
        return "RPRT 0";
      }

      var shared =
        new CwMessageLeaseManager(Radio);
      var first =
        new CwMessageCommandSession(
          Radio,
          shared);
      var second =
        new CwMessageCommandSession(
          Radio,
          shared);

      Assert.Equal(
        "RPRT 0",
        first.Execute(SendA));
      Assert.Equal(
        "RPRT -6",
        second.Execute(SendB));

      // A bystander ABORT is acknowledged locally but never forwarded.
      Assert.Equal(
        "RPRT 0",
        second.Execute(Abort));
      Assert.Equal(
        new[] { SendA },
        writes);

      Assert.Equal(
        "RPRT 0",
        first.Execute(Abort));
      Assert.Equal(
        new[] { SendA, Abort },
        writes);

      Assert.Equal(
        "RPRT 0",
        second.Execute(SendB));
    }

    [Fact]
    public void DisconnectAbortsOnlyOwningClient()
    {
      var writes =
        new List<string>();

      string Radio(string command)
      {
        writes.Add(command);
        return "RPRT 0";
      }

      var shared =
        new CwMessageLeaseManager(Radio);
      var owner =
        new CwMessageCommandSession(
          Radio,
          shared);
      var bystander =
        new CwMessageCommandSession(
          Radio,
          shared);

      Assert.Equal(
        "RPRT 0",
        owner.Execute(SendA));

      bystander.EnsureCwAbort();
      Assert.Equal(
        new[] { SendA },
        writes);

      owner.EnsureCwAbort();
      Assert.Equal(
        new[] { SendA, Abort },
        writes);
      Assert.False(shared.HasOwner);
    }

    [Fact]
    public void AmbiguousSendFailureAttemptsImmediateAbort()
    {
      var writes =
        new List<string>();

      string Radio(string command)
      {
        writes.Add(command);
        return command == SendA
          ? "RPRT -5"
          : "RPRT 0";
      }

      var shared =
        new CwMessageLeaseManager(Radio);
      var client =
        new CwMessageCommandSession(
          Radio,
          shared);

      Assert.Equal(
        "RPRT -5",
        client.Execute(SendA));
      Assert.Equal(
        new[] { SendA, Abort },
        writes);
      Assert.False(shared.HasOwner);
    }

    [Fact]
    public void FailedAbortKeepsOrphanedLeaseUntilRecovery()
    {
      var writes =
        new List<string>();
      bool abortWorks = false;

      string Radio(string command)
      {
        writes.Add(command);

        if (command == Abort)
          return abortWorks
            ? "RPRT 0"
            : "RPRT -6";

        return "RPRT 0";
      }

      var shared =
        new CwMessageLeaseManager(Radio);
      var first =
        new CwMessageCommandSession(
          Radio,
          shared);
      var second =
        new CwMessageCommandSession(
          Radio,
          shared);

      Assert.Equal(
        "RPRT 0",
        first.Execute(SendA));

      first.EnsureCwAbort();
      Assert.True(shared.HasOwner);
      Assert.True(shared.IsOrphaned);

      Assert.Equal(
        "RPRT -6",
        second.Execute(SendB));
      Assert.True(shared.HasOwner);

      abortWorks = true;
      shared.RetryUncertainRelease();

      Assert.False(shared.HasOwner);
      Assert.Equal(
        "RPRT 0",
        second.Execute(SendB));
    }

    [Fact]
    public void NonCwCommandsStayOnPerClientForwardChain()
    {
      var sharedWrites =
        new List<string>();
      var clientWrites =
        new List<string>();

      string Shared(string command)
      {
        sharedWrites.Add(command);
        return "RPRT 0";
      }

      string Client(string command)
      {
        clientWrites.Add(command);
        return "CLIENT";
      }

      var shared =
        new CwMessageLeaseManager(Shared);
      var session =
        new CwMessageCommandSession(
          Client,
          shared);

      Assert.Equal(
        "CLIENT",
        session.Execute("f"));
      Assert.Equal(
        new[] { "f" },
        clientWrites);
      Assert.Empty(sharedWrites);
    }
  }
}
