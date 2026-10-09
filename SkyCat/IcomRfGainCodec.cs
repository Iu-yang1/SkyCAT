namespace SkyCat
{
  /// <summary>
  /// IC-9700 CI-V 14 02 RF gain: two packed BCD bytes, range 0000..0255.
  /// Codec deliberately does not support 14 01 (hardware AF gain) because
  /// SkyRoof controls RS-BA1's Windows playback session for AF instead.
  /// </summary>
  public static class IcomRfGainCodec
  {
    public static byte[] Encode(int gain)
    {
      if (gain is < 0 or > 255)
        throw new ArgumentOutOfRangeException(nameof(gain));

      return new byte[] {
        (byte)(gain / 100),
        (byte)((((gain / 10) % 10) << 4) | (gain % 10))
      };
    }

    public static int Decode(ReadOnlySpan<byte> bytes)
    {
      if (bytes.Length != 2 ||
          bytes[0] > 0x02 ||
          (bytes[0] & 0xF0) != 0 ||
          (bytes[1] >> 4) > 9 ||
          (bytes[1] & 0x0F) > 9)
        throw new FormatException("Malformed IC-9700 RF gain BCD.");

      int value = bytes[0] * 100 +
        (bytes[1] >> 4) * 10 + (bytes[1] & 0x0F);
      if (value > 255)
        throw new FormatException("IC-9700 RF gain is outside 0..255.");
      return value;
    }

    public static byte[] BuildWrite(int gain)
    {
      byte[] bcd = Encode(gain);
      return new byte[] {
        0xFE, 0xFE, 0xA2, 0xE0, 0x14, 0x02,
        bcd[0], bcd[1], 0xFD
      };
    }

    public static byte[] BuildRead() =>
      new byte[] {
        0xFE, 0xFE, 0xA2, 0xE0, 0x14, 0x02, 0xFD
      };

    public static bool TryExtractReadReply(
      ReadOnlySpan<byte> frame, out int value)
    {
      value = 0;
      if (frame.Length != 9 ||
          frame[0] != 0xFE || frame[1] != 0xFE ||
          frame[2] != 0xE0 || frame[3] != 0xA2 ||
          frame[4] != 0x14 || frame[5] != 0x02 ||
          frame[8] != 0xFD)
        return false;

      value = Decode(frame.Slice(6, 2));
      return true;
    }
  }
}
