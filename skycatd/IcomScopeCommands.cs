using System.Globalization;
using Microsoft.Extensions.Logging;
using SkyCat;

namespace skycatd
{
  /// <summary>
  /// The textual U SCOPE_* protocol consumed by SkyRoof. All commands are
  /// serialized by CatServer.commandLock along with ordinary CAT traffic.
  /// Full CI-V 27 xx command semantics are documented in the IC-9700 guide.
  /// </summary>
  public static class IcomScopeCommands
  {
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Execute(CatCommandSender sender, string[] args)
    {
      if (!string.Equals(sender.RadioName, "IC-9700",
          StringComparison.OrdinalIgnoreCase))
        return "RPRT -11";

      try
      {
        if (args.Length == 2 && args[1] == "SCOPE_READ")
          return ReadAll(sender);

        if (args.Length == 4 && args[1] == "SCOPE_READ_EDGE")
        {
          byte range = IcomScopeCivCodec.CheckedNumber(args[2], 1, 3);
          byte edge = IcomScopeCivCodec.CheckedNumber(args[3], 1, 4);
          var value = sender.ReadIcomScope(0x1E, 10, range, edge);
          long lower = IcomScopeCivCodec.DecodeFrequency(value.AsSpan(0, 5));
          long upper = IcomScopeCivCodec.DecodeFrequency(value.AsSpan(5, 5));
          if (lower >= upper)
            throw new FormatException("Invalid fixed-edge readback.");
          return $"RANGE={range};EDGE={edge};LOWER={lower};UPPER={upper}";
        }

        (byte command, byte[] payload) = BuildWrite(args);
        sender.WriteIcomScope(command, payload);
        return "RPRT 0";
      }
      catch (ArgumentException)
      {
        return "RPRT -1";
      }
      catch (InvalidReplyException)
      {
        return "RPRT -9";
      }
      catch (TimeoutException)
      {
        return "RPRT -5";
      }
      catch (InvalidOperationException)
      {
        return "RPRT -6";
      }
      catch (NotSupportedException)
      {
        return "RPRT -11";
      }
      catch (FormatException)
      {
        return "RPRT -9";
      }
      catch (Exception ex)
      {
        sender.Log?.LogError(ex, "IC-9700 scope command failed.");
        return "RPRT -7";
      }
    }

    // Validates all arguments BEFORE sending any CI-V bytes. Exposed to unit
    // tests, so invalid values can never accidentally reach the transmitter.
    public static (byte Subcommand, byte[] Payload) BuildWrite(string[] a)
    {
      if (a.Length < 3 || a[0] != "U")
        throw new ArgumentException("Expected a scope write command.");

      string op = a[1];
      if (op == "SCOPE_SELECT" && a.Length == 3)
        return (0x12, new[] { IcomScopeCivCodec.ScopeNameToIndex(a[2]) });

      if (op == "SCOPE_MODE" && a.Length == 4)
        return (0x14, new[] {
          IcomScopeCivCodec.ScopeNameToIndex(a[2]),
          IcomScopeCivCodec.ModeFromString(a[3]) });

      if (op == "SCOPE_SPAN" && a.Length == 4)
      {
        byte index = IcomScopeCivCodec.ScopeNameToIndex(a[2]);
        long span = long.Parse(a[3], NumberStyles.None, Invariant);
        return (0x15, new[] { index }.Concat(IcomScopeCivCodec.EncodeSpan(span)).ToArray());
      }

      if (op == "SCOPE_EDGE" && a.Length == 4)
        return (0x16, new[] {
          IcomScopeCivCodec.ScopeNameToIndex(a[2]),
          IcomScopeCivCodec.CheckedNumber(a[3], 1, 4) });

      if (op == "SCOPE_REF" && a.Length == 4)
      {
        byte index = IcomScopeCivCodec.ScopeNameToIndex(a[2]);
        double db = double.Parse(a[3], NumberStyles.Float, Invariant);
        return (0x19, new[] { index }.Concat(IcomScopeCivCodec.EncodeReference(db)).ToArray());
      }

      if (op == "SCOPE_SPEED" && a.Length == 4)
        return (0x1A, new[] {
          IcomScopeCivCodec.ScopeNameToIndex(a[2]),
          IcomScopeCivCodec.SpeedFromString(a[3]) });

      if (op == "SCOPE_VBW" && a.Length == 4)
        return (0x1D, new[] {
          IcomScopeCivCodec.ScopeNameToIndex(a[2]),
          IcomScopeCivCodec.VbwFromString(a[3]) });

      if (op == "SCOPE_TX" && a.Length == 3)
        return (0x1B, new[] { Flag(a[2]) });

      if (op == "SCOPE_CENTER_TYPE" && a.Length == 3)
        return (0x1C, new[] { IcomScopeCivCodec.CenterFromString(a[2]) });

      if (op == "SCOPE_MARKER" && a.Length == 3)
        return (0x20, new[] { IcomScopeCivCodec.MarkerFromString(a[2]) });

      if (op == "SCOPE_FIXED_EDGE" && a.Length == 6)
      {
        byte range = IcomScopeCivCodec.CheckedNumber(a[2], 1, 3);
        byte edge = IcomScopeCivCodec.CheckedNumber(a[3], 1, 4);
        long low = long.Parse(a[4], NumberStyles.None, Invariant);
        long high = long.Parse(a[5], NumberStyles.None, Invariant);

        // Icom's fixed-edge frequency parser ignores sub-kHz digits; do
        // not let an invalid/swap-reversed range be written to the rig.
        if (low < 0 || high <= low || high > 1_300_000_000 ||
            low < 144_000_000 ||
            (range switch {
              1 => low < 144_000_000 || high > 148_000_000,
              2 => low < 430_000_000 || high > 450_000_000,
              3 => low < 1_240_000_000 || high > 1_300_000_000,
              _ => true }))
          throw new ArgumentException("Fixed edges outside IC-9700 band limits.");

        return (0x1E, new[] { range, edge }
          .Concat(IcomScopeCivCodec.EncodeFrequency(low))
          .Concat(IcomScopeCivCodec.EncodeFrequency(high)).ToArray());
      }

      throw new ArgumentException("Unrecognized scope operation or argument count.");
    }

    private static byte Flag(string a) => a switch {
      "0" => 0, "1" => 1,
      _ => throw new ArgumentException("Expected 0 or 1.")
    };

    private static string ReadAll(CatCommandSender sender)
    {
      // Query the actual scope state instead of manufacturing defaults. A
      // failed/malformed reply prevents SkyRoof from marking sync complete.
      byte selected = OnlyValue(sender, 0x12);
      string selectedName = IcomScopeCivCodec.FormatScope(selected);
      var parts = new List<string> { $"SELECT={selectedName}" };

      for (byte index = 0; index <= 1; index++)
      {
        string name = IcomScopeCivCodec.FormatScope(index);
        parts.Add($"{name}.MODE={IcomScopeCivCodec.FormatMode(OnlyValue(sender, 0x14, index))}");
        parts.Add($"{name}.SPAN={IcomScopeCivCodec.DecodeSpan(sender.ReadIcomScope(0x15, 5, index))}");
        byte edge = OnlyValue(sender, 0x16, index);
        if (edge < 1 || edge > 4) throw new FormatException("Invalid scope edge number.");
        parts.Add($"{name}.EDGE={edge}");
        double reference = IcomScopeCivCodec.DecodeReference(sender.ReadIcomScope(0x19, 3, index));
        parts.Add($"{name}.REF={reference.ToString("0.0", Invariant)}");
        parts.Add($"{name}.SPEED={IcomScopeCivCodec.FormatSpeed(OnlyValue(sender, 0x1A, index))}");
        parts.Add($"{name}.VBW={IcomScopeCivCodec.FormatVbw(OnlyValue(sender, 0x1D, index))}");
      }

      parts.Add($"TX={FlagFromRig(OnlyValue(sender, 0x1B))}");
      parts.Add($"CENTER={IcomScopeCivCodec.FormatCenter(OnlyValue(sender, 0x1C))}");
      parts.Add($"MARKER={IcomScopeCivCodec.FormatMarker(OnlyValue(sender, 0x20))}");
      return string.Join(";", parts);
    }

    private static byte OnlyValue(CatCommandSender sender, byte op, params byte[] selectors) =>
      sender.ReadIcomScope(op, 1, selectors)[0];

    private static byte FlagFromRig(byte value) =>
      value is 0 or 1 ? value :
        throw new FormatException("Invalid scope ON/OFF flag.");
  }
}
