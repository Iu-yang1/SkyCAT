using System.Globalization;

namespace SkyCat
{
  /// <summary>
  /// Strict IC-9700 CI-V 1A 05 01 xx readback codec.
  /// IPv4 uses four 4-digit, big-endian packed-BCD octets; ports use 6 BCD digits.
  /// </summary>
  public static class IcomNetworkCivCodec
  {
    public static bool TryExtractQueryReply(
      ReadOnlySpan<byte> frame, byte setting, int expectedValueBytes,
      out byte[] value)
    {
      value = Array.Empty<byte>();
      if (expectedValueBytes is not (1 or 3 or 8) ||
          frame.Length != 9 + expectedValueBytes ||
          frame[0] != 0xFE || frame[1] != 0xFE ||
          frame[2] != 0xE0 || frame[3] != 0xA2 ||
          frame[4] != 0x1A || frame[5] != 0x05 ||
          frame[6] != 0x01 || frame[7] != setting ||
          frame[^1] != 0xFD)
        return false;
      value = frame.Slice(8, expectedValueBytes).ToArray();
      return true;
    }

    private static int DecimalPair(byte value)
    {
      int high = value >> 4, low = value & 0x0F;
      if (high > 9 || low > 9)
        throw new FormatException("Malformed network BCD data.");
      return high * 10 + low;
    }

    public static string DecodeIPv4(ReadOnlySpan<byte> value)
    {
      if (value.Length != 8)
        throw new FormatException("Network IPv4 response must contain eight BCD bytes.");
      string[] octets = new string[4];
      for (int i = 0; i < 4; i++)
      {
        int number = DecimalPair(value[2 * i]) * 100 +
          DecimalPair(value[2 * i + 1]);
        if (number > 255)
          throw new FormatException("Network IPv4 octet exceeds 255.");
        octets[i] = number.ToString(CultureInfo.InvariantCulture);
      }
      return string.Join(".", octets);
    }

    public static int DecodePort(ReadOnlySpan<byte> value)
    {
      if (value.Length != 3)
        throw new FormatException("Network UDP port response must contain three BCD bytes.");
      int port = DecimalPair(value[0]) * 10000 +
        DecimalPair(value[1]) * 100 + DecimalPair(value[2]);
      if (port is < 1 or > 65535)
        throw new FormatException("Network UDP port outside 1..65535.");
      return port;
    }

    public static bool DecodeEnabled(ReadOnlySpan<byte> value)
    {
      if (value.Length != 1 || value[0] is not (0x00 or 0x01))
        throw new FormatException("Malformed Network Control readback.");
      return value[0] == 0x01;
    }
  }
}
