using Microsoft.Extensions.Logging;

namespace skycatd
{
  /// <summary>
  /// A deliberately restricted rigctld-compatible command surface for WSJT-X.
  /// Reads and CAT PTT reach the radio. Other write commands are acknowledged
  /// as no-ops so WSJT-X/Hamlib never competes with SkyRoof for radio state.
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

      // Hamlib NET rigctl performs these while opening the connection.
      if (line == "\\chk_vfo" || line == "chk_vfo") return "0";
      if (line == "\\dump_state" || line == "dump_state") return DumpState;

      var args = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      string op = args[0];

      // Long-form rigctl setters are all safe-sunk except PTT.
      if (op.StartsWith("\\set_", StringComparison.Ordinal))
      {
        if (op == "\\set_ptt")
          return args.Length == 2 ? SetPtt(args[1]) : "RPRT -1";

        Logger?.LogDebug($"WSJT-X write acknowledged as no-op: '{line}'");
        return "RPRT 0";
      }

      return op switch
      {
        // Read-only state exposed to WSJT-X.
        "f" => Forward("f"),
        "\\get_freq" => Forward("f"),
        "i" => Forward("i"),
        "\\get_split_freq" => Forward("i"),
        "m" => ReadMode("m"),
        "\\get_mode" => ReadMode("m"),
        "x" => ReadMode("x"),
        "\\get_split_mode" => ReadMode("x"),
        "t" => ReadPtt(),
        "\\get_ptt" => ReadPtt(),

        // Stable logical VFO/split view. These are not forwarded to the radio.
        "v" or "\\get_vfo" => "VFOA",
        "s" or "\\get_split_vfo" => "0\nVFOB",

        // Useful identity query; does not touch the radio.
        "\\get_info" => "SkyCAT WSJT-X compatibility proxy",

        // CAT PTT is the only write that reaches the radio.
        "T" when args.Length == 2 => SetPtt(args[1]),

        // WSJT-X/Hamlib can issue these during Test CAT, setup changes or
        // band changes. Acknowledge them without forwarding so SkyRoof remains
        // the sole controller of frequency, mode, VFO, split, tone, etc.
        "F" or "I" or "M" or "X" or "V" or "S" or "C" or "U"
          or "J" or "Z" or "L" or "P" or "G" or "H" or "A" or "Y"
          or "R" or "O" or "D" => SafeNoOp(line),

        // A polite no-op for clients that explicitly close a rigctl session.
        "q" or "\\quit" => "RPRT 0",

        _ => "RPRT -11"
      };
    }

    private string SafeNoOp(string command)
    {
      Logger?.LogDebug($"WSJT-X write acknowledged as no-op: '{command}'");
      return "RPRT 0";
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

      // rigctld get_mode/get_split_mode returns mode and passband width.
      // SkyCAT does not track the selected filter width, so report 0 ("normal").
      return $"{reply}\n0";
    }

    private string SetPtt(string value)
    {
      // Hamlib ptt_t: 0=OFF, 1=ON, 2=ON_MIC, 3=ON_DATA.
      // IC-9700/SkyCAT has a binary CAT PTT, so all non-zero variants map to ON.
      if (!int.TryParse(value, out int ptt) || ptt < 0 || ptt > 3)
        return "RPRT -1";

      string normalized = ptt == 0 ? "0" : "1";
      string reply = Forward($"T {normalized}");
      if (reply == "RPRT 0") PttAsserted = normalized == "1";
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
    // Write capabilities are intentionally advertised where Hamlib/WSJT-X expects
    // them, but the proxy safe-sinks those writes instead of changing the radio.
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
      "ptt_type=0x5\n" +       // CAT PTT, MIC/DATA variants supported
      "targetable_vfo=0x0\n" +
      "has_set_vfo=1\n" +      // compatibility no-op
      "has_get_vfo=1\n" +
      "has_set_freq=1\n" +     // compatibility no-op
      "has_get_freq=1\n" +
      "timeout=1500\n" +
      "rig_model=3081\n" +
      "rigctld_version=SkyCAT-WSJTX-2\n" +
      "done";
  }
}
