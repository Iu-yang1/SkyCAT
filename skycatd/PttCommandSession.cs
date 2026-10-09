using Microsoft.Extensions.Logging;

namespace skycatd
{
  /// <summary>
  /// Tracks PTT asserted by one main CAT TCP connection. A disconnect only
  /// releases PTT that this connection successfully asserted.
  /// </summary>
  public sealed class PttCommandSession
  {
    private readonly Func<string, string> Forward;
    private readonly ILogger? Logger;
    private readonly string Name;
    private bool PttAsserted;
    private readonly PttLeaseManager? SharedLease;

    public PttCommandSession(
      Func<string, string> forward,
      ILogger? logger = null,
      string name = "CAT",
      PttLeaseManager? sharedLease = null)
    {
      Forward = forward;
      Logger = logger;
      Name = name;
      SharedLease = sharedLease;
    }

    public string Execute(string command)
    {
      string reply = SharedLease == null
        ? Forward(command)
        : SharedLease.Execute(this, command);
      string[] args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

      if (reply == "RPRT 0" && args.Length == 2 && args[0] == "T")
      {
        if (args[1] == "1") PttAsserted = true;
        else if (args[1] == "0") PttAsserted = false;
      }

      return reply;
    }

    public void EnsurePttOff()
    {
      if (SharedLease != null)
      {
        SharedLease.Release(this);
        return;
      }
      if (!PttAsserted) return;

      try
      {
        Forward("T 0");
      }
      catch (Exception ex)
      {
        Logger?.LogWarning($"Failed to release {Name} PTT after disconnect: {ex.Message}");
      }
      finally
      {
        PttAsserted = false;
      }
    }
  }
}
