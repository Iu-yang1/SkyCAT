using System.Text;

namespace SkyCat;

public static class IcomCwMessageCodec
{
  public const int MaxCharacters = 30;

  private static readonly HashSet<char> Allowed =
    new(
      "0123456789" +
      "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
      "abcdefghijklmnopqrstuvwxyz" +
      "/?.-,:\'()=+\"@ ^");

  public static byte[] BuildSend(string text)
  {
    Validate(text);

    byte[] ascii =
      Encoding.ASCII.GetBytes(text);
    byte[] frame =
      new byte[6 + ascii.Length];

    frame[0] = 0xFE;
    frame[1] = 0xFE;
    frame[2] = 0xA2;
    frame[3] = 0xE0;
    frame[4] = 0x17;
    ascii.CopyTo(frame, 5);
    frame[^1] = 0xFD;
    return frame;
  }

  public static byte[] BuildStop() =>
  [
    0xFE, 0xFE,
    0xA2, 0xE0,
    0x17, 0xFF,
    0xFD
  ];

  public static void Validate(string text)
  {
    if (string.IsNullOrEmpty(text))
      throw new ArgumentException(
        "CW text must not be empty.",
        nameof(text));

    if (text.Length > MaxCharacters)
      throw new ArgumentException(
        $"CW text exceeds {MaxCharacters} characters.",
        nameof(text));

    for (int i = 0;
         i < text.Length;
         i++)
    {
      char ch = text[i];
      if (!Allowed.Contains(ch))
        throw new ArgumentException(
          $"CW character U+{(int)ch:X4} at index {i} is not supported by IC-9700 Command 17.",
          nameof(text));
    }
  }
}
