using Microsoft.Extensions.Logging;

namespace skycatd
{
  /// <summary>
  /// A deliberately restricted rigctld-compatible command surface for WSJT-X.
  /// It exposes read-only radio state plus CAT PTT, while SkyRoof remains the
  /// authoritative controller for frequency, mode, VFO, split and satellite state.
  /// </summary>
  public class WsjtXCommandInterpreter
  {
    private readonly Func<string, string> Forward;
    private readonly ILogger? Logger;
    private bool PttAsserted;

    public WsjtXCommandInterpreter(Func<string, string> forward, ILogger? logger = null)
    {
      Forward = forward;
      Logger = logger;
    }

    public string Execute(string command)
    {
      string line = command.Trim();
      if (line.Length == 0) return "RPRT -1";

      // Hamlib NET rigctl performs these two queries while opening the connection.
      if (line == "\\chk_vfo" || line == "chk_vfo") return "0";
      if (line == "\\dump_state" || line == "dump_state") return DumpState;

      var args = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      string op = args[0];

      return op switch
      {
        // Read-only state exposed to WSJT-X.
        "f" => Forward("f"),
        "\\get_freq" => Forward("f"),
        "i" => Forward("i"),
        "m" => ReadMode("m"),
        "\\get_mode" => ReadMode("m"),
        "x" => ReadMode("x"),
        "t" => ReadPtt(),
        "\\get_ptt" => ReadPtt(),

        // Hamlib may query these even when WSJT-X is configured with Split=None.
        // We intentionally present a stable, non-VFO-mode, non-split view.
        "v" or "\\get_vfo" => "VFOA",
        "s" or "\\get_split_vfo" => "0\nVFOB",

        // WSJT-X Test CAT writes the just-read dial frequency back to the rig.
        // Accept a near-identical write as a no-op so the test can complete,
        // but do not let WSJT-X take over SkyRoof's Doppler tuning.
        "F" when args.Length == 2 && long.TryParse(args[1], out var rxFrequency)
          => AcceptNearCurrentFrequency(rxFrequency),

        // The only radio-changing operation allowed on the WSJT-X port is PTT.
        "T" when args.Length == 2 && (args[1] == "0" || args[1] == "1")
          => SetPtt(args[1]),
        "\\set_ptt" when args.Length == 2 && (args[1] == "0" || args[1] == "1")
          => SetPtt(args[1]),

        // A polite no-op for clients that explicitly close a rigctl session.
        "q" or "\\quit" => "RPRT 0",

        // Frequency/mode/VFO/split/tone writes are intentionally unavailable.
        _ => "RPRT -11"
      };
    }

    private string ReadPtt()
    {
      string reply = Forward("t").Trim();
      return reply switch
      {
        "ON" => "1",
        "OFF" => "0",
        _ => reply
      };
    }

    private string ReadMode(string command)
    {
      string reply = Forward(command).Trim();
      if (reply.StartsWith("RPRT ", StringComparison.Ordinal)) return reply;

      // rigctld get_mode returns two records: mode and passband width.
      // SkyCAT does not track the selected filter width, so report 0 ("normal").
      return $"{reply}\n0";
    }

    private string AcceptNearCurrentFrequency(long requestedFrequency)
    {
      const long toleranceHz = 250;

      string currentReply = Forward("f").Trim();
      if (!long.TryParse(currentReply, out long currentFrequency))
        return currentReply.StartsWith("RPRT ", StringComparison.Ordinal)
          ? currentReply
          : "RPRT -9";

      long delta = Math.Abs(requestedFrequency - currentFrequency);
      if (delta <= toleranceHz)
      {
        Logger?.LogDebug(
          $"WSJT-X frequency write accepted as no-op: requested={requestedFrequency}, current={currentFrequency}, delta={delta} Hz.");
        return "RPRT 0";
      }

      Logger?.LogWarning(
        $"WSJT-X frequency write blocked: requested={requestedFrequency}, current={currentFrequency}, delta={delta} Hz.");
      return "RPRT -11";
    }

    private string SetPtt(string value)
    {
      string reply = Forward($"T {value}");
      if (reply == "RPRT 0") PttAsserted = value == "1";
      return reply;
    }

    /// <summary>
    /// Releases CAT PTT if this proxy asserted it. Intended for client disconnect.
    /// </summary>
    public void EnsurePttOff()
    {
      if (!PttAsserted) return;

      try
      {
        Forward("T 0");
      }
      catch (Exception ex)
      {
        Logger?.LogWarning($"Failed to release WSJT-X PTT after disconnect: {ex.Message}");
      }
      finally
      {
        PttAsserted = false;
      }
    }

    // Minimal rigctld protocol-v1 state accepted by Hamlib NET rigctl.
    // The fixed protocol-v0 section must be present even when no ranges/steps
    // are advertised. Protocol-v1 fields then describe the restricted surface.
    internal const string DumpState =
      "1\n" +                  // protocol version
      "3081\n" +               // underlying radio model (IC-9700)
      "0\n" +                  // deprecated ITU region
      "0 0 0 0 0 0 0\n" +    // end RX ranges
      "0 0 0 0 0 0 0\n" +    // end TX ranges
      "0 0\n" +                // end tuning steps
      "0 0\n" +                // end filters
      "0\n" +                  // max RIT
      "0\n" +                  // max XIT
      "0\n" +                  // max IF shift
      "0\n" +                  // announces
      "\n" +                   // preamps
      "\n" +                   // attenuators
      "0x0\n" +                // has_get_func
      "0x0\n" +                // has_set_func
      "0x0\n" +                // has_get_level
      "0x0\n" +                // has_set_level
      "0x0\n" +                // has_get_parm
      "0x0\n" +                // has_set_parm
      "ptt_type=0x5\n" +       // CAT PTT, mic/data capable
      "targetable_vfo=0x0\n" +
      "has_set_vfo=0\n" +
      "has_get_vfo=1\n" +
      "has_set_freq=1\n" +     // compatibility no-op for near-current writes only
      "has_get_freq=1\n" +
      "timeout=1000\n" +
      "rig_model=3081\n" +
      "rigctld_version=SkyCAT-WSJTX-1\n" +
      "done";
  }
}
