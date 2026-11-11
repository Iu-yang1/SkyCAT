using SkyCat;

namespace skycatd
{
  public static class IcomMeterCommands
  {
    public static string Execute(CatCommandSender sender, string field)
    {
      if (!string.Equals(sender.RadioName, "IC-9700",
            StringComparison.OrdinalIgnoreCase))
        return "RPRT -11";
      byte kind = field switch
      {
        "S" => 0x02,
        "PWR" => 0x11,
        "SWR" => 0x12,
        "ALC" => 0x13,
        _ => (byte)0
      };
      if (kind == 0) return "RPRT -1";
      try
      {
        int raw = sender.ReadIcomMeter(kind);
        return field + "=" + raw.ToString(
          System.Globalization.CultureInfo.InvariantCulture);
      }
      catch (InvalidReplyException ex)
      {
        sender.Log?.LogWarning("IC-9700 meter read rejected: {Message}", ex.Message);
        return "RPRT -9";
      }
      catch (TimeoutException ex)
      {
        sender.Log?.LogDebug("IC-9700 meter read timed out: {Message}", ex.Message);
        return "RPRT -5";
      }
      catch (InvalidOperationException ex)
      {
        sender.Log?.LogDebug("IC-9700 meter read unavailable: {Message}", ex.Message);
        return "RPRT -6";
      }
      catch (NotSupportedException)
      {
        return "RPRT -11";
      }
    }
  }
}
