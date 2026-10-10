using skycatd;

namespace SkyCat.Tests;

public sealed class OptionsTests
{
  [Fact]
  public void CwPort_MustDifferFromEveryActiveEndpoint()
  {
    var options =
      BaseOptions();

    options.CwPort = options.Port;
    Assert.False(options.Validate());

    options = BaseOptions();
    options.CwPort = options.WsjtXPort;
    Assert.False(options.Validate());

    options = BaseOptions();
    options.CwPort = options.ScopePort;
    Assert.False(options.Validate());

    options = BaseOptions();
    options.CwPort = options.SwitchPort;
    Assert.False(options.Validate());
  }

  [Fact]
  public void DisabledEndpoints_DoNotReserveTheirConfiguredPort()
  {
    var options =
      BaseOptions();

    options.DisableWsjtXProxy = true;
    options.CwPort = options.WsjtXPort;
    Assert.True(options.Validate());

    options = BaseOptions();
    options.DisableSwitchPort = true;
    options.CwPort = options.SwitchPort;
    Assert.True(options.Validate());
  }

  [Fact]
  public void DisablingCwPort_RemovesCwConflictValidation()
  {
    var options =
      BaseOptions();

    options.DisableCwPort = true;
    options.CwPort = options.Port;

    Assert.True(options.Validate());
  }

  private static Options BaseOptions() =>
    new()
    {
      Model = "IC-9700",
      RigFile = "COM9",
      Port = 4532,
      WsjtXPort = 4534,
      ScopePort = 4535,
      SwitchPort = 4537,
      CwPort = 4538
    };
}
