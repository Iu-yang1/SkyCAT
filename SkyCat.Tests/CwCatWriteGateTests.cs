using skycatd;

namespace SkyCat.Tests;

public sealed class CwCatWriteGateTests
{
  [Theory]
  [InlineData("I 145900000")]
  [InlineData("X CW 0")]
  [InlineData("C 670")]
  [InlineData("U TONE 1")]
  [InlineData("U TONE 0")]
  [InlineData("S 1 Sub")]
  [InlineData("S 1 VFOB")]
  [InlineData("S 0 VFOA")]
  [InlineData("U SATMODE 1")]
  [InlineData("U Duplex")]
  [InlineData("U Split")]
  [InlineData("U Simplex")]
  [InlineData("U Transmitter")]
  public void ActiveCwLease_BlocksTxStateWrites(
    string command)
  {
    bool forwarded = false;
    var gate =
      new CwCatWriteGate(
        () => true,
        value =>
        {
          forwarded = true;
          return "RPRT 0";
        });

    Assert.Equal(
      "RPRT -6",
      gate.Execute(command));
    Assert.False(forwarded);
  }

  [Theory]
  [InlineData("F 435605000")]
  [InlineData("M CW 0")]
  [InlineData("f")]
  [InlineData("m")]
  [InlineData("i")]
  [InlineData("x")]
  [InlineData("t")]
  [InlineData("U RF_GAIN 128")]
  [InlineData("U SCOPE_DATA 1")]
  public void ActiveCwLease_AllowsRxWritesAndReads(
    string command)
  {
    var forwarded = new List<string>();
    var gate =
      new CwCatWriteGate(
        () => true,
        value =>
        {
          forwarded.Add(value);
          return "FORWARDED";
        });

    Assert.Equal(
      "FORWARDED",
      gate.Execute(command));
    Assert.Equal(
      new[] { command },
      forwarded);
  }

  [Fact]
  public void IdleCwLease_DoesNotChangeCatBehavior()
  {
    string[] commands =
    [
      "I 145900000",
      "X CW 0",
      "C 670",
      "U TONE 1",
      "S 1 Sub"
    ];

    var forwarded = new List<string>();
    var gate =
      new CwCatWriteGate(
        () => false,
        value =>
        {
          forwarded.Add(value);
          return "RPRT 0";
        });

    foreach (string command in commands)
      Assert.Equal(
        "RPRT 0",
        gate.Execute(command));

    Assert.Equal(
      commands,
      forwarded);
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("U SCOPE 1")]
  [InlineData("U DUAL_WATCH 0")]
  public void NonTxCommands_AreNotClassifiedAsBlocked(
    string command)
  {
    Assert.False(
      CwCatWriteGate.IsTxStateWrite(
        command));
  }
}
