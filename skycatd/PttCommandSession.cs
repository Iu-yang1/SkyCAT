using Microsoft.Extensions.Logging;

namespace skycatd
{
  /// <summary>
  /// Tracks PTT asserted by one main CAT TCP connection. A disconnect only
  /// releases PTT that this connection successfully asserted.
  /// </summary>
  internal sealed class PttCommandSession
  {
    private readonly Func<string, string> Forward;
    private readonly ILogger? Logger;
    private readonly string Name;
    private bool PttAsserted;

    internal PttCommandSession(
      Func<string, string> forward,
      ILogger? logger = null,
      string name = "CAT")
    {
      Forward = forward;
      Logger = logger;
      Name = name;
    }

    internal string Execute(string command)
    {
      string reply = Forward(command);
      string[] args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

      if (reply == "RPRT 0" && args.Length == 2 && args[0] == "T")
      {
        if (args[1] == "1") PttAsserted = true;
        else if (args[1] == "0") PttAsserted = false;
      }

      return reply;
    }

    internal void EnsurePttOff()
    {
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
