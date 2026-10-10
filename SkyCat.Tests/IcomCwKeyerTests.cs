using SkyCat;
using skycatd;

namespace SkyCat.Tests;

public sealed class IcomCwKeyerTests
{
  [Fact]
  public void Codec_BuildsExactSendAndBinaryStopFrames()
  {
    byte[] send =
      IcomCwMessageCodec.BuildSend(
        "CQ DE BG5JSU");

    Assert.Equal(
      new byte[]
      {
        0xFE, 0xFE, 0xA2, 0xE0, 0x17,
        (byte)'C', (byte)'Q', (byte)' ',
        (byte)'D', (byte)'E', (byte)' ',
        (byte)'B', (byte)'G', (byte)'5',
        (byte)'J', (byte)'S', (byte)'U',
        0xFD
      },
      send);

    Assert.Equal(
      new byte[]
      {
        0xFE, 0xFE, 0xA2, 0xE0,
        0x17, 0xFF, 0xFD
      },
      IcomCwMessageCodec.BuildStop());
  }

  [Fact]
  public void Codec_EnforcesThirtyCharactersAndIcomAlphabet()
  {
    IcomCwMessageCodec.Validate(
      "012345678901234567890123456789");

    Assert.Throws<ArgumentException>(
      () =>
        IcomCwMessageCodec.Validate(
          "0123456789012345678901234567890"));

    Assert.Throws<ArgumentException>(
      () =>
        IcomCwMessageCodec.Validate(
          "CQ
TEST"));

    IcomCwMessageCodec.Validate(
      "CQ^TEST?");
  }

  [Fact]
  public void Send_RequiresCwModeBreakInAndIdleTransmitter()
  {
    var fixture =
      new Fixture();

    fixture.Mode = "USB";
    Assert.Equal(
      "ERR MODE",
      fixture.Keyer.Execute(
        fixture.CwClient,
        "SEND CQ"));

    fixture.Mode = "CW";
    fixture.BreakIn = 0;
    Assert.Equal(
      "ERR BREAKIN",
      fixture.Keyer.Execute(
        fixture.CwClient,
        "SEND CQ"));

    fixture.BreakIn = 1;
    fixture.HardwarePtt = "1";
    Assert.Equal(
      "ERR TXACTIVE",
      fixture.Keyer.Execute(
        fixture.CwClient,
        "SEND CQ"));

    Assert.Empty(
      fixture.SentMessages);
    Assert.Equal(0, fixture.StopCalls);
  }

  [Fact]
  public void CwLease_BlocksPttUntilExplicitStop()
  {
    var fixture =
      new Fixture();

    Assert.Equal(
      "OK",
      fixture.Keyer.Execute(
        fixture.CwClient,
        "SEND CQ TEST"));

    Assert.Equal(
      new[] { "CQ TEST" },
      fixture.SentMessages);

    Assert.Equal(
      "RPRT -6",
      fixture.Ptt.Execute(
        fixture.PttClient,
        "T 1"));

    Assert.Equal(
      "ERR BUSY",
      fixture.Keyer.Execute(
        fixture.OtherCwClient,
        "STOP"));

    Assert.Equal(
      "OK",
      fixture.Keyer.Execute(
        fixture.CwClient,
        "STOP"));

    Assert.Equal(1, fixture.StopCalls);

    Assert.Equal(
      "RPRT 0",
      fixture.Ptt.Execute(
        fixture.PttClient,
        "T 1"));
    Assert.Equal(
      "RPRT 0",
      fixture.Ptt.Execute(
        fixture.PttClient,
        "T 0"));
  }

  [Fact]
  public void ExistingPttLease_BlocksCwSend()
  {
    var fixture =
      new Fixture();

    Assert.Equal(
      "RPRT 0",
      fixture.Ptt.Execute(
        fixture.PttClient,
        "T 1"));

    Assert.Equal(
      "ERR BUSY",
      fixture.Keyer.Execute(
        fixture.CwClient,
        "SEND CQ"));

    Assert.Empty(
      fixture.SentMessages);

    fixture.Ptt.Execute(
      fixture.PttClient,
      "T 0");
  }

  [Fact]
  public void AmbiguousSend_PerformsFailSafeStopBeforeReleasingLease()
  {
    var fixture =
      new Fixture
      {
        SendFailure =
          new TimeoutException(
            "simulated")
      };

    Assert.Equal(
      "ERR TIMEOUT",
      fixture.Keyer.Execute(
        fixture.CwClient,
        "SEND CQ"));

    Assert.Equal(1, fixture.StopCalls);
    Assert.False(
      fixture.Keyer.HasLease);

    Assert.Equal(
      "RPRT 0",
      fixture.Ptt.Execute(
        fixture.PttClient,
        "T 1"));
    fixture.Ptt.Execute(
      fixture.PttClient,
      "T 0");
  }

  [Fact]
  public void DisconnectStopFailure_RemainsFailClosedUntilRetrySucceeds()
  {
    var fixture =
      new Fixture();

    Assert.Equal(
      "OK",
      fixture.Keyer.Execute(
        fixture.CwClient,
        "SEND CQ"));

    fixture.StopFailure =
      new TimeoutException(
        "simulated stop timeout");

    fixture.Keyer.Release(
      fixture.CwClient);

    Assert.True(
      fixture.Keyer.HasLease);
    Assert.Equal(
      "RPRT -6",
      fixture.Ptt.Execute(
        fixture.PttClient,
        "T 1"));

    fixture.StopFailure = null;
    fixture.Keyer.RetryUncertainRelease();

    Assert.False(
      fixture.Keyer.HasLease);
    Assert.Equal(
      "RPRT 0",
      fixture.Ptt.Execute(
        fixture.PttClient,
        "T 1"));
    fixture.Ptt.Execute(
      fixture.PttClient,
      "T 0");
  }

  [Fact]
  public void Status_ReportsRadioPreflightAndLeaseState()
  {
    var fixture =
      new Fixture();

    Assert.Equal(
      "STATUS IDLE MODE=CW BKIN=1 TX=0 KEYRAW=128",
      fixture.Keyer.Execute(
        fixture.CwClient,
        "STATUS"));

    Assert.Equal(
      "OK",
      fixture.Keyer.Execute(
        fixture.CwClient,
        "SEND CQ"));

    Assert.Equal(
      "STATUS OWNED MODE=CW BKIN=1 TX=0 KEYRAW=128",
      fixture.Keyer.Execute(
        fixture.CwClient,
        "STATUS"));

    fixture.Keyer.Execute(
      fixture.CwClient,
      "STOP");
  }

  private sealed class Fixture
  {
    public object CwClient { get; } =
      new();
    public object OtherCwClient { get; } =
      new();
    public object PttClient { get; } =
      new();

    public string Mode { get; set; } =
      "CW";
    public int BreakIn { get; set; } =
      1;
    public string HardwarePtt { get; set; } =
      "0";
    public int KeySpeedRaw { get; set; } =
      128;

    public Exception? SendFailure {
      get; set;
    }
    public Exception? StopFailure {
      get; set;
    }

    public List<string> SentMessages { get; } =
      [];
    public int StopCalls { get; private set; }

    public PttLeaseManager Ptt { get; }
    public IcomCwKeyerLeaseManager Keyer { get; }

    public Fixture()
    {
      Ptt =
        new PttLeaseManager(
          command =>
            command is "T 1" or "T 0"
              ? "RPRT 0"
              : "RPRT -11");

      Keyer =
        new IcomCwKeyerLeaseManager(
          sendCat: command =>
            command switch
            {
              CatCommand.read_tx_mode =>
                Mode,
              CatCommand.read_ptt =>
                HardwarePtt,
              _ =>
                throw new InvalidOperationException(
                  "Unexpected CAT command.")
            },
          readBreakIn: () =>
            BreakIn,
          readKeySpeedRaw: () =>
            KeySpeedRaw,
          sendCw: text =>
          {
            if (SendFailure != null)
              throw SendFailure;
            SentMessages.Add(text);
          },
          stopCw: () =>
          {
            StopCalls++;
            if (StopFailure != null)
              throw StopFailure;
          },
          pttLease: Ptt);
    }
  }
}
