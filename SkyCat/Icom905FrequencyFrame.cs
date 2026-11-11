using System;
using System.Globalization;
using System.Linq;

namespace SkyCat
{
  /// <summary>
  /// CI-V command 25 on IC-905 uses five BCD frequency bytes below 10 GHz
  /// and six bytes in the 10 GHz band. Only tagged command-25 entries
  /// may vary their payload size; other CI-V fields remain unchanged.
  /// </summary>
  internal static class Icom905FrequencyFrame
  {
    private const long ExtendedBandStartHz = 10_000_000_000L;

    internal static bool RequiresSixBytes(string frequencyHz)
    {
      if (!long.TryParse(frequencyHz, NumberStyles.None,
            CultureInfo.InvariantCulture, out long parsed))
        throw new ArgumentException("CI-V frequency must be an unsigned integer in Hz.",
          nameof(frequencyHz));

      return parsed >= ExtendedBandStartHz;
    }

    internal static byte[] ComposeCommand(
      byte?[] template, byte[] parameterBytes)
    {
      int start = Array.IndexOf(template, null);
      int originalWidth = template.Count(value => value == null);
      if (originalWidth != 5 || start < 0 ||
          (parameterBytes.Length != 5 && parameterBytes.Length != 6))
        throw new ArgumentException("IC-905 command-25 frequency template must have five BCD placeholders.");

      var command = new byte[template.Length + parameterBytes.Length - originalWidth];
      for (int i = 0; i < start; i++)
        command[i] = template[i]!.Value;

      parameterBytes.CopyTo(command, start);
      int tail = start + originalWidth;
      for (int i = tail; i < template.Length; i++)
        command[start + parameterBytes.Length + i - tail] =
          template[i]!.Value;

      return command;
    }

    internal static bool MatchesSixByteReply(
      byte[] frame, byte?[] template, out int frequencyStart)
    {
      frequencyStart = Array.IndexOf(template, null);
      if (frequencyStart < 0 || template.Count(b => b == null) != 5 ||
          frame.Length != template.Length + 1)
        return false;

      int end = frequencyStart + 5;
      for (int i = 0; i < frequencyStart; i++)
        if (template[i] != frame[i])
          return false;

      for (int i = end; i < template.Length; i++)
        if (template[i] != frame[i + 1])
          return false;

      long frequency = 0;
      long multiplier = 1;
      for (int i = 0; i < 6; i++)
      {
        byte digit = frame[frequencyStart + i];
        int tens = digit >> 4;
        int ones = digit & 0x0F;
        if (tens > 9 || ones > 9)
          return false;
        frequency += (tens * 10L + ones) * multiplier;
        multiplier *= 100;
      }

      return frequency >= ExtendedBandStartHz;
    }
  }
}
