using System.Text;

namespace SkyCat
{
  /// <summary>
  /// Strict IC-9700 CI-V CW keyer codec.
  ///
  /// CI-V command 17 accepts at most 30 printable CW-message characters.
  /// A single FF data byte aborts queued CW transmission. Keying speed is
  /// command 14 0C with a 0000..0255 packed-BCD level corresponding to
  /// 6..48 WPM.
  /// </summary>
  public static class IcomCwKeyerCodec
  {
    public const int MaxMessageCharacters = 30;
    public const int MinWpm = 6;
    public const int MaxWpm = 48;

    private static readonly HashSet<char> AllowedPunctuation =
      new([
        '/',
        '?',
        '.',
        '-',
        ',',
        ':',
        '\'',
        '(',
        ')',
        '=',
        '+',
        '"',
        '@',
        ' ',
        '^'
      ]);

    public static byte[] EncodeMessage(string text)
    {
      if (string.IsNullOrEmpty(text))
        throw new ArgumentException(
          "CW message must contain at least one character.",
          nameof(text));
      if (text.Length > MaxMessageCharacters)
        throw new ArgumentException(
          $"CW message is limited to {MaxMessageCharacters} characters per CI-V frame.",
          nameof(text));

      byte[] bytes =
        Encoding.ASCII.GetBytes(text);

      for (int i = 0; i < text.Length; i++)
      {
        char ch = text[i];
        bool allowed =
          ch is >= '0' and <= '9' ||
          ch is >= 'A' and <= 'Z' ||
          ch is >= 'a' and <= 'z' ||
          AllowedPunctuation.Contains(ch);

        if (!allowed || bytes[i] == 0xFF)
          throw new ArgumentException(
            $"Unsupported IC-9700 CW character U+{(int)ch:X4} at position {i}.",
            nameof(text));
      }

      return bytes;
    }

    public static byte[] BuildSend(string text)
    {
      byte[] payload =
        EncodeMessage(text);
      byte[] frame =
        new byte[6 + payload.Length];

      frame[0] = 0xFE;
      frame[1] = 0xFE;
      frame[2] = 0xA2;
      frame[3] = 0xE0;
      frame[4] = 0x17;
      payload.CopyTo(frame, 5);
      frame[^1] = 0xFD;
      return frame;
    }

    public static byte[] BuildAbort() =>
      [
        0xFE, 0xFE, 0xA2, 0xE0,
        0x17, 0xFF, 0xFD
      ];

    public static int WpmToLevel(int wpm)
    {
      if (wpm is < MinWpm or > MaxWpm)
        throw new ArgumentOutOfRangeException(
          nameof(wpm),
          $"CW speed must be {MinWpm}..{MaxWpm} WPM.");

      return (int)Math.Round(
        (wpm - MinWpm) *
        255.0 /
        (MaxWpm - MinWpm),
        MidpointRounding.AwayFromZero);
    }

    public static int LevelToWpm(int level)
    {
      if (level is < 0 or > 255)
        throw new ArgumentOutOfRangeException(
          nameof(level));

      return (int)Math.Round(
        MinWpm +
        level *
        (MaxWpm - MinWpm) /
        255.0,
        MidpointRounding.AwayFromZero);
    }

    public static byte[] EncodeLevel(int level)
    {
      if (level is < 0 or > 255)
        throw new ArgumentOutOfRangeException(
          nameof(level));

      return
      [
        (byte)(level / 100),
        (byte)(
          (((level / 10) % 10) << 4) |
          (level % 10))
      ];
    }

    public static byte[] BuildSpeedWrite(int wpm)
    {
      byte[] bcd =
        EncodeLevel(
          WpmToLevel(wpm));

      return
      [
        0xFE, 0xFE, 0xA2, 0xE0,
        0x14, 0x0C,
        bcd[0], bcd[1],
        0xFD
      ];
    }
  }
}
