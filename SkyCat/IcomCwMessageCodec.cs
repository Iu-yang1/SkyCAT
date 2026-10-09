using System.Text;

namespace SkyCat
{
  /// <summary>
  /// Strict IC-9700 CI-V command 17 codec.
  ///
  /// The radio accepts at most 30 CW characters. 0xFF is not text: it is the
  /// documented stop-sending sentinel, so callers can never place it in a
  /// message payload through this codec.
  /// </summary>
  public static class IcomCwMessageCodec
  {
    public const int MaxCharacters = 30;

    private static readonly HashSet<byte> Allowed =
      BuildAllowedCharacters();

    public static byte[] BuildSendFrame(
      string text)
    {
      ArgumentNullException.ThrowIfNull(text);

      byte[] payload =
        Encoding.ASCII.GetBytes(text);

      if (payload.Length == 0 ||
          payload.Length >
            MaxCharacters)
        throw new ArgumentException(
          $"CW message must contain 1..{MaxCharacters} ASCII characters.",
          nameof(text));

      if (text.Any(ch => ch > 0x7F))
        throw new ArgumentException(
          "CW message contains a non-ASCII character.",
          nameof(text));

      ValidatePayload(payload);

      byte[] frame =
        new byte[payload.Length + 6];
      frame[0] = 0xFE;
      frame[1] = 0xFE;
      frame[2] = 0xA2;
      frame[3] = 0xE0;
      frame[4] = 0x17;
      payload.CopyTo(frame, 5);
      frame[^1] = 0xFD;
      return frame;
    }

    public static byte[] BuildAbortFrame() =>
    [
      0xFE, 0xFE,
      0xA2, 0xE0,
      0x17, 0xFF,
      0xFD
    ];

    public static string DecodeBase64Payload(
      string encoded)
    {
      if (string.IsNullOrWhiteSpace(
            encoded))
        throw new ArgumentException(
          "CW message payload is blank.",
          nameof(encoded));

      byte[] payload;
      try
      {
        payload =
          Convert.FromBase64String(
            encoded);
      }
      catch (FormatException ex)
      {
        throw new ArgumentException(
          "CW message payload is not valid Base64.",
          nameof(encoded),
          ex);
      }

      if (payload.Length == 0 ||
          payload.Length >
            MaxCharacters)
        throw new ArgumentException(
          $"CW message must contain 1..{MaxCharacters} characters.",
          nameof(encoded));

      ValidatePayload(payload);
      return Encoding.ASCII.GetString(
        payload);
    }

    public static string EncodeBase64Payload(
      string text)
    {
      byte[] frame =
        BuildSendFrame(text);
      byte[] payload =
        frame.AsSpan(
          5,
          frame.Length - 6)
        .ToArray();
      return Convert.ToBase64String(
        payload);
    }

    public static bool IsAllowedCharacter(
      char value) =>
      value <= 0x7F &&
      Allowed.Contains((byte)value);

    private static void ValidatePayload(
      ReadOnlySpan<byte> payload)
    {
      for (int i = 0;
           i < payload.Length;
           i++)
      {
        byte value = payload[i];
        if (!Allowed.Contains(value))
          throw new ArgumentException(
            $"Unsupported IC-9700 CW character 0x{value:X2} at index {i}.");
      }
    }

    private static HashSet<byte>
      BuildAllowedCharacters()
    {
      var result =
        new HashSet<byte>();

      for (byte value = 0x30;
           value <= 0x39;
           value++)
        result.Add(value);

      for (byte value = 0x41;
           value <= 0x5A;
           value++)
        result.Add(value);

      for (byte value = 0x61;
           value <= 0x7A;
           value++)
        result.Add(value);

      foreach (char value in
        " /?.-,:\'()=+\"@")
        result.Add((byte)value);

      return result;
    }
  }
}
