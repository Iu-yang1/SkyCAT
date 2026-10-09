namespace skycatd
{
  /// <summary>
  /// Observes IC-9700 CI-V 27 00 sequence numbers without touching the
  /// waveform payload. A sweep is counted only after every numbered
  /// fragment arrives in order for the same MAIN/SUB receiver.
  /// </summary>
  internal sealed class IcomScopeSweepMeter
  {
    private readonly object sync = new();
    private readonly int[] expected = new int[2];
    private readonly int[] maximum = new int[2];
    private long completed;
    private long incomplete;
    private long invalid;

    internal (long Completed, long Incomplete, long Invalid) Snapshot()
    {
      lock (sync) return (completed, incomplete, invalid);
    }

    internal void Record(ReadOnlySpan<byte> frame)
    {
      if (frame.Length < 10 || frame[0] != 0xFE ||
          frame[1] != 0xFE || frame[4] != 0x27 ||
          frame[5] != 0x00 || frame[^1] != 0xFD ||
          frame[6] > 1 ||
          !DecodeBcd(frame[7], out int seq) ||
          !DecodeBcd(frame[8], out int max) ||
          seq < 1 || max < 1 || max > 11 || seq > max)
      {
        lock (sync) invalid++;
        return;
      }

      int rx = frame[6];
      lock (sync)
      {
        if (seq == 1)
        {
          if (expected[rx] > 1) incomplete++;
          if (max == 1)
          {
            completed++;
            expected[rx] = 0;
          }
          else
          {
            expected[rx] = 2;
            maximum[rx] = max;
          }
        }
        else if (expected[rx] == seq && maximum[rx] == max)
        {
          if (seq == max)
          {
            completed++;
            expected[rx] = 0;
          }
          else
            expected[rx]++;
        }
        else
        {
          incomplete++;
          expected[rx] = 0;
        }
      }
    }

    private static bool DecodeBcd(byte b, out int number)
    {
      int tens = b >> 4, ones = b & 15;
      number = tens * 10 + ones;
      return tens <= 9 && ones <= 9;
    }
  }
}
