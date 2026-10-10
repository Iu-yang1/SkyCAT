using SkyCat;

namespace skycatd;

/// <summary>
/// Prevents a normal CAT client from changing transmitter state while the
/// dedicated Command-17 CW keyer owns the shared transmitter lease.
///
/// RX frequency/mode writes remain available in IC-9700 duplex satellite
/// operation so downlink tracking can continue. Only TX-sensitive writes and
/// radio operating-mode reconfiguration are fail-closed.
/// </summary>
public sealed class CwCatWriteGate
{
  private readonly Func<bool> CwLeaseActive;
  private readonly Func<string, string> Forward;

  public CwCatWriteGate(
    Func<bool> cwLeaseActive,
    Func<string, string> forward)
  {
    CwLeaseActive =
      cwLeaseActive ??
      throw new ArgumentNullException(
        nameof(cwLeaseActive));
    Forward =
      forward ??
      throw new ArgumentNullException(
        nameof(forward));
  }

  public string Execute(
    string command)
  {
    if (CwLeaseActive() &&
        IsTxStateWrite(command))
      return "RPRT -6";

    return Forward(command);
  }

  public static bool IsTxStateWrite(
    string command)
  {
    string[] args =
      command.Split(
        ' ',
        StringSplitOptions.RemoveEmptyEntries);

    if (args.Length == 0)
      return false;

    // rigctld split/TX frequency and mode.
    if (args[0] is "I" or "X")
      return true;

    // CTCSS transmit encoder state/tone.
    if (args[0] == "C")
      return true;

    if (args[0] == "U" &&
        args.Length >= 2 &&
        args[1] == "TONE")
      return true;

    // Reconfiguring simplex/split/duplex/SAT mode can change which physical
    // VFO/sub receiver is the transmitter, so it is forbidden mid-message.
    if (args[0] == "S")
      return true;

    if (args[0] == "U" &&
        args.Length >= 2 &&
        (args[1] == "SATMODE" ||
         Enum.TryParse<OperatingMode>(
           args[1],
           ignoreCase: true,
           out _)))
      return true;

    return false;
  }
}
