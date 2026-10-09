using System.Globalization;
using SkyCat;

namespace skycatd;

/// <summary>
/// A deliberately small, exclusive IC-9700 auxiliary-settings API, served on
/// localhost:4536. No frequency, VFO selection, SAT write, mode, PTT, raw
/// CI-V, or arbitrary CAT commands are accepted by this port.
/// Each request uses CatServer's shared commandLock.
/// Protocol: GET NAME => VALUE uppercase-hex; SET NAME uppercase-hex => OK.
/// </summary>
public static class IcomSwitchCommands
{
  private sealed record Setting(byte[] Selector, int DataBytes, byte Max,
    bool Writable, bool Bcd255 = false);

  private static readonly IReadOnlyDictionary<string, Setting> Settings =
    new Dictionary<string, Setting>(StringComparer.Ordinal)
    {
      ["DATA_OFF"] = new([0x1A, 0x05, 0x01, 0x15], 1, 5, true),
      ["DATA_MOD"] = new([0x1A, 0x05, 0x01, 0x16], 1, 5, true),
      ["USB_OUTPUT"] = new([0x1A, 0x05, 0x01, 0x05], 1, 1, true),
      ["COMP"] = new([0x16, 0x44], 1, 1, true),
      ["COMP_LEVEL"] = new([0x14, 0x0E], 2, 255, true, true),
      ["KEY_SPEED"] = new([0x14, 0x0C], 2, 255, true, true),
      ["RF_POWER"] = new([0x14, 0x0A], 2, 255, true, true),
      ["SAT_MODE"] = new([0x16, 0x5A], 1, 1, false)
    };

  public static (byte[] Selector, int DataBytes, bool Writable) Describe(string name)
  {
    if (!Settings.TryGetValue(name, out var setting))
      throw new ArgumentException("Unknown auxiliary setting.", nameof(name));
    return (setting.Selector.ToArray(), setting.DataBytes, setting.Writable);
  }

  public static byte[] ParseAndValidateValue(string name, string hex,
    bool writing = true)
  {
    if (!Settings.TryGetValue(name, out var setting))
      throw new ArgumentException("Unknown auxiliary setting.", nameof(name));
    if (writing && !setting.Writable)
      throw new ArgumentException("This setting is read-only.", nameof(name));
    if (hex.Length != setting.DataBytes * 2 || !hex.All(Uri.IsHexDigit))
      throw new ArgumentException("Invalid CI-V value length or hexadecimal digits.");
    byte[] data = Convert.FromHexString(hex);
    if (!setting.Bcd255)
    {
      if (data.Length != 1 || data[0] > setting.Max)
        throw new ArgumentException("Setting outside supported range.");
    }
    else
    {
      // The IC-9700 command 14 encoding is four decimal digits 0000..0255.
      if (data.Length != 2 ||
          (data[0] >> 4) > 9 || (data[0] & 15) > 9 ||
          (data[1] >> 4) > 9 || (data[1] & 15) > 9)
        throw new ArgumentException("Malformed numeric CI-V BCD.");
      int raw = ((data[0] >> 4) * 1000) +
                ((data[0] & 15) * 100) +
                ((data[1] >> 4) * 10) + (data[1] & 15);
      if (raw > setting.Max)
        throw new ArgumentException("Numeric CI-V value exceeds 0255.");
    }
    return data;
  }

  public static string Execute(CatCommandSender sender, string request)
  {
    var parts = request.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 2 && parts[0] == "GET" &&
        Settings.TryGetValue(parts[1], out var getSetting))
    {
      try
      {
        byte[] data = sender.ReadIcomSwitchSetting(
          getSetting.Selector, getSetting.DataBytes);
        ParseAndValidateValue(parts[1], Convert.ToHexString(data), false);
        return "VALUE " + Convert.ToHexString(data);
      }
      catch (NotSupportedException) { return "ERR UNSUPPORTED"; }
      catch (InvalidOperationException) { return "ERR DISCONNECTED"; }
      catch (TimeoutException) { return "ERR TIMEOUT"; }
      catch (Exception) { return "ERR RADIO"; }
    }

    if (parts.Length == 3 && parts[0] == "SET" &&
        Settings.TryGetValue(parts[1], out var setSetting))
    {
      byte[] data;
      try { data = ParseAndValidateValue(parts[1], parts[2]); }
      catch (ArgumentException) { return "ERR INVALID"; }
      try
      {
        sender.WriteIcomSwitchSetting(setSetting.Selector, data);
        return "OK";
      }
      catch (NotSupportedException) { return "ERR UNSUPPORTED"; }
      catch (InvalidOperationException) { return "ERR DISCONNECTED"; }
      catch (TimeoutException) { return "ERR TIMEOUT"; }
      catch (Exception) { return "ERR RADIO"; }
    }
    return "ERR INVALID";
  }
}
