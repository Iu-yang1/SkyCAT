namespace SkyCat
{
  /// <summary>
  /// IC-9700 read-only CI-V 15 xx meter response. These are relative BCD
  /// levels, not calibrated RF watts or an absolute audio measurement.
  /// </summary>
  public static class IcomMeterCivCodec
  {
    public static bool ValidMeter(byte kind) =>
      kind is 0x02 or 0x11 or 0x12 or 0x13;

    public static byte[] BuildRead(byte kind)
    {
      if (!ValidMeter(kind))
        throw new ArgumentOutOfRangeException(nameof(kind));
      return [0xFE, 0xFE, 0xA2, 0xE0, 0x15, kind, 0xFD];
    }

    public static bool TryDecode(
      ReadOnlySpan<byte> frame, byte expectedKind, out int value)
    {
      value = 0;
      if (!ValidMeter(expectedKind) || frame.Length != 9 ||
          frame[0] != 0xFE || frame[1] != 0xFE ||
          (frame[2] != 0xE0 && frame[2] != 0x00) ||
          frame[3] != 0xA2 || frame[4] != 0x15 ||
          frame[5] != expectedKind || frame[8] != 0xFD ||
          !ValidBcd(frame[6]) || !ValidBcd(frame[7]))
        return false;
      int raw = (frame[6] >> 4) * 1000 +
                (frame[6] & 0x0F) * 100 +
                (frame[7] >> 4) * 10 + (frame[7] & 0x0F);
      if (raw > 255) return false;
      value = raw;
      return true;
    }

    private static bool ValidBcd(byte value) =>
      (value >> 4) < 10 && (value & 0x0F) < 10;
  }
}
