using Microsoft.Extensions.Logging;
using SkyCat;

namespace skycatd
{
  /// <summary>
  /// Read-only IC-9700 network register access for SkyRoof configuration.
  /// Each call shares the current CAT command lock and never creates a LAN session.
  /// </summary>
  public static class IcomNetworkCommands
  {
    public static string Execute(CatCommandSender sender, string field)
    {
      if (!string.Equals(sender.RadioName, "IC-9700",
          StringComparison.OrdinalIgnoreCase))
        return "RPRT -11";

      (byte register, int size) = field switch
      {
        "IP" => ((byte)0x39, 8),
        "ENABLED" => ((byte)0x45, 1),
        "CONTROL" => ((byte)0x47, 3),
        "SERIAL" => ((byte)0x48, 3),
        "AUDIO" => ((byte)0x49, 3),
        _ => ((byte)0, 0)
      };
      if (size == 0) return "RPRT -1";

      try
      {
        byte[] raw = sender.ReadIcomNetwork(register, size);
        string decoded = field switch
        {
          "IP" => IcomNetworkCivCodec.DecodeIPv4(raw),
          "ENABLED" => IcomNetworkCivCodec.DecodeEnabled(raw) ? "1" : "0",
          _ => IcomNetworkCivCodec.DecodePort(raw).ToString(
            System.Globalization.CultureInfo.InvariantCulture)
        };
        return field + "=" + decoded;
      }
      catch (InvalidReplyException ex)
      {
        sender.Log?.LogWarning("IC-9700 network read rejected: {Error}", ex.Message);
        return "RPRT -9";
      }
      catch (TimeoutException ex)
      {
        sender.Log?.LogWarning("IC-9700 network read timed out: {Error}", ex.Message);
        return "RPRT -5";
      }
      catch (InvalidOperationException ex)
      {
        sender.Log?.LogWarning("IC-9700 network read unavailable: {Error}", ex.Message);
        return "RPRT -6";
      }
      catch (NotSupportedException)
      {
        return "RPRT -11";
      }
      catch (FormatException ex)
      {
        sender.Log?.LogWarning("Malformed IC-9700 network readback: {Error}", ex.Message);
        return "RPRT -9";
      }
    }
  }
}
