using System.Globalization;

namespace SkyCat
{
  /// <summary>
  /// IC-9700 CI-V 27 xx scope payload values. Commands follow Icom's
  /// IC-9700 CI-V Reference Guide, scope command format pp. 24-25.
  /// Frequency fields use five LE-BCD bytes; reference level uses two
  /// packed BCD bytes followed by a sign byte.
  /// </summary>
  public static class IcomScopeCivCodec
  {
    private static readonly HashSet<long> ValidSpans =
      new() { 2500, 5000, 10000, 25000, 50000, 100000, 250000, 500000 };

    public static byte[] EncodeFrequency(long hz)
    {
      if (hz < 0 || hz > 9_999_999_999)
        throw new ArgumentOutOfRangeException(nameof(hz));
      byte[] result = new byte[5];
      for (int i = 0; i < result.Length; i++)
      {
        int pair = (int)(hz % 100);
        result[i] = (byte)(((pair / 10) << 4) | pair % 10);
        hz /= 100;
      }
      return result;
    }

    public static long DecodeFrequency(ReadOnlySpan<byte> bytes)
    {
      if (bytes.Length != 5) throw new FormatException("Five BCD bytes required.");
      long hz = 0, place = 1;
      foreach (byte b in bytes)
      {
        int hi = b >> 4, lo = b & 0x0f;
        if (hi > 9 || lo > 9) throw new FormatException("Malformed BCD frequency.");
        hz += (hi * 10 + lo) * place;
        place *= 100;
      }
      return hz;
    }

    public static byte[] EncodeSpan(long hz)
    {
      if (!ValidSpans.Contains(hz))
        throw new ArgumentOutOfRangeException(nameof(hz), "Unsupported IC-9700 scope span.");
      return EncodeFrequency(hz);
    }

    public static long DecodeSpan(ReadOnlySpan<byte> bcd)
    {
      long span = DecodeFrequency(bcd);
      if (!ValidSpans.Contains(span))
        throw new FormatException($"Unknown IC-9700 scope span {span}.");
      return span;
    }

    public static byte[] EncodeReference(double db)
    {
      if (!double.IsFinite(db) || db < -20 || db > 20 ||
          Math.Abs(db * 2 - Math.Round(db * 2)) > 0.00001)
        throw new ArgumentOutOfRangeException(nameof(db),
          "Reference must be -20..20 dB in half-dB increments.");
      int tenths = (int)Math.Round(Math.Abs(db) * 10);
      int whole = tenths / 10;
      return new byte[] {
        (byte)(((whole / 10) << 4) | whole % 10),
        (byte)((tenths % 10) << 4),
        (byte)(db < 0 ? 1 : 0)
      };
    }

    public static double DecodeReference(ReadOnlySpan<byte> payload)
    {
      if (payload.Length != 3) throw new FormatException("Scope REF uses 3 data bytes.");
      int high = payload[0] >> 4, low = payload[0] & 0x0f;
      int fraction = payload[1] >> 4;
      if (high > 2 || low > 9 || fraction is not (0 or 5) ||
          (payload[1] & 0x0f) != 0 || payload[2] > 1)
        throw new FormatException("Malformed CI-V scope reference.");
      double value = high * 10 + low + fraction / 10.0;
      if (value > 20) throw new FormatException("Scope REF is outside -20..20 dB.");
      return payload[2] == 1 ? -value : value;
    }

    public static byte ScopeNameToIndex(string text) =>
      text.ToUpperInvariant() switch {
        "MAIN" => 0, "SUB" => 1,
        _ => throw new ArgumentException("Expected MAIN or SUB.") };

    public static string FormatScope(byte index) => index switch {
      0 => "MAIN", 1 => "SUB",
      _ => throw new FormatException("Invalid IC-9700 scope index.") };

    public static byte ModeFromString(string text) =>
      text.ToUpperInvariant() switch {
        "CENTER" => 0, "FIXED" => 1,
        "SCROLL-C" => 2, "SCROLL-F" => 3,
        _ => throw new ArgumentException("Unknown IC-9700 scope mode.") };

    public static string FormatMode(byte value) => value switch {
      0 => "CENTER", 1 => "FIXED", 2 => "SCROLL-C", 3 => "SCROLL-F",
      _ => throw new FormatException("Invalid scope mode reply.") };

    public static byte SpeedFromString(string text) =>
      text.ToUpperInvariant() switch {
        "FAST" => 0, "MID" => 1, "SLOW" => 2,
        _ => throw new ArgumentException("Unknown IC-9700 sweep speed.") };

    public static string FormatSpeed(byte speed) => speed switch {
      0 => "FAST", 1 => "MID", 2 => "SLOW",
      _ => throw new FormatException("Invalid sweep speed reply.") };

    public static byte VbwFromString(string text) =>
      text.ToUpperInvariant() switch {
        "NARROW" => 0, "WIDE" => 1,
        _ => throw new ArgumentException("Unknown scope VBW.") };

    public static string FormatVbw(byte v) => v switch {
      0 => "NARROW", 1 => "WIDE",
      _ => throw new FormatException("Invalid VBW reply.") };

    public static byte CenterFromString(string text) =>
      text.ToUpperInvariant() switch {
        "FILTER" => 0, "CARRIER" => 1, "ABS" => 2,
        _ => throw new ArgumentException("Unknown CENTER type.") };

    public static string FormatCenter(byte v) => v switch {
      0 => "FILTER", 1 => "CARRIER", 2 => "ABS",
      _ => throw new FormatException("Invalid CENTER type reply.") };

    public static byte MarkerFromString(string text) =>
      text.ToUpperInvariant() switch {
        "FILTER" => 0, "CARRIER" => 1,
        _ => throw new ArgumentException("Unknown marker position.") };

    public static string FormatMarker(byte v) => v switch {
      0 => "FILTER", 1 => "CARRIER",
      _ => throw new FormatException("Invalid marker position reply.") };

    public static byte CheckedNumber(string value, int min, int max)
    {
      if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
          out int number) || number < min || number > max)
        throw new ArgumentException($"Number must be {min}..{max}.");
      return (byte)number;
    }
  }
}
