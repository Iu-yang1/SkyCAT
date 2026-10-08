namespace SkyCat
{
  public enum IcomScopeReceiver : byte
  {
    Main = 0,
    Sub = 1
  }

  public enum IcomScopeMode : byte
  {
    Center = 0,
    Fixed = 1,
    ScrollCenter = 2,
    ScrollFixed = 3
  }

  public enum IcomScopeSweepSpeed : byte
  {
    Fast = 0,
    Mid = 1,
    Slow = 2
  }

  public sealed record IcomScopeSettings(
    IcomScopeReceiver Receiver,
    IcomScopeMode Mode,
    long SpanHz,
    int EdgeNumber,
    bool Hold,
    int ReferenceLevelTenthsDb,
    IcomScopeSweepSpeed SweepSpeed);

  /// <summary>
  /// IC-9700 CI-V 0x27 spectrum-scope codec. This deliberately exposes typed
  /// scope operations rather than a generic raw-CI-V tunnel.
  /// </summary>
  public static class IcomScopeProtocol
  {
    public const byte RadioAddress = 0xA2;
    public const byte ControllerAddress = 0xE0;

    public const byte Command = 0x27;
    public const byte ModeSubcommand = 0x14;
    public const byte SpanSubcommand = 0x15;
    public const byte EdgeSubcommand = 0x16;
    public const byte HoldSubcommand = 0x17;
    public const byte ReferenceSubcommand = 0x19;
    public const byte SpeedSubcommand = 0x1A;

    private static readonly HashSet<long> ValidSpans =
      new()
      {
        2_500,
        5_000,
        10_000,
        25_000,
        50_000,
        100_000,
        250_000,
        500_000
      };

    public static byte[] BuildQuery(
      IcomScopeReceiver receiver,
      byte subcommand)
    {
      ValidateReceiver(receiver);
      ValidateSubcommand(subcommand);

      return
      [
        0xFE, 0xFE,
        RadioAddress,
        ControllerAddress,
        Command,
        subcommand,
        (byte)receiver,
        0xFD
      ];
    }

    public static byte[] BuildSet(
      IcomScopeReceiver receiver,
      byte subcommand,
      ReadOnlySpan<byte> value)
    {
      ValidateReceiver(receiver);
      ValidateSubcommand(subcommand);

      var frame =
        new byte[8 + value.Length];

      frame[0] = 0xFE;
      frame[1] = 0xFE;
      frame[2] = RadioAddress;
      frame[3] = ControllerAddress;
      frame[4] = Command;
      frame[5] = subcommand;
      frame[6] = (byte)receiver;
      value.CopyTo(frame.AsSpan(7));
      frame[^1] = 0xFD;

      return frame;
    }

    public static byte[] EncodeMode(
      IcomScopeMode mode)
    {
      if ((byte)mode > 3)
        throw new ArgumentOutOfRangeException(nameof(mode));

      return [(byte)mode];
    }

    public static byte[] EncodeSpan(long spanHz)
    {
      if (!ValidSpans.Contains(spanHz))
        throw new ArgumentOutOfRangeException(
          nameof(spanHz),
          "IC-9700 scope span must be 2.5, 5, 10, 25, 50, 100, 250 or 500 kHz.");

      return EncodeBcdLittleEndian(
        spanHz,
        5);
    }

    public static byte[] EncodeEdge(int edgeNumber)
    {
      if (edgeNumber is < 1 or > 4)
        throw new ArgumentOutOfRangeException(nameof(edgeNumber));

      return [(byte)edgeNumber];
    }

    public static byte[] EncodeHold(bool hold) =>
      [hold ? (byte)1 : (byte)0];

    public static byte[] EncodeReferenceLevel(
      int tenthsDb)
    {
      if (tenthsDb is < -200 or > 200 ||
          Math.Abs(tenthsDb) % 5 != 0)
        throw new ArgumentOutOfRangeException(
          nameof(tenthsDb),
          "IC-9700 scope reference level must be -20.0..+20.0 dB in 0.5 dB steps.");

      int magnitude =
        Math.Abs(tenthsDb);

      int wholeDb =
        magnitude / 10;
      int decimalTenths =
        magnitude % 10;

      return
      [
        ToBcdByte(wholeDb),
        (byte)(decimalTenths << 4),
        tenthsDb < 0
          ? (byte)1
          : (byte)0
      ];
    }

    public static byte[] EncodeSpeed(
      IcomScopeSweepSpeed speed)
    {
      if ((byte)speed > 2)
        throw new ArgumentOutOfRangeException(nameof(speed));

      return [(byte)speed];
    }

    public static IcomScopeMode DecodeMode(
      ReadOnlySpan<byte> value)
    {
      RequireLength(
        value,
        1,
        nameof(value));

      if (value[0] > 3)
        throw new InvalidReplyException(
          $"Invalid IC-9700 scope mode 0x{value[0]:X2}");

      return
        (IcomScopeMode)value[0];
    }

    public static long DecodeSpan(
      ReadOnlySpan<byte> value)
    {
      RequireLength(
        value,
        5,
        nameof(value));

      long span =
        DecodeBcdLittleEndian(value);

      if (!ValidSpans.Contains(span))
        throw new InvalidReplyException(
          $"Invalid IC-9700 scope span {span} Hz");

      return span;
    }

    public static int DecodeEdge(
      ReadOnlySpan<byte> value)
    {
      RequireLength(
        value,
        1,
        nameof(value));

      if (value[0] is < 1 or > 4)
        throw new InvalidReplyException(
          $"Invalid IC-9700 scope edge {value[0]}");

      return value[0];
    }

    public static bool DecodeHold(
      ReadOnlySpan<byte> value)
    {
      RequireLength(
        value,
        1,
        nameof(value));

      return value[0] switch
      {
        0 => false,
        1 => true,
        _ => throw new InvalidReplyException(
          $"Invalid IC-9700 scope hold value 0x{value[0]:X2}")
      };
    }

    public static int DecodeReferenceLevel(
      ReadOnlySpan<byte> value)
    {
      RequireLength(
        value,
        3,
        nameof(value));

      int wholeDb =
        FromBcdByte(value[0]);

      int decimalHigh =
        (value[1] >> 4) & 0x0F;
      int decimalLow =
        value[1] & 0x0F;

      if ((decimalHigh != 0 &&
           decimalHigh != 5) ||
          decimalLow != 0 ||
          value[2] > 1)
        throw new InvalidReplyException(
          $"Invalid IC-9700 scope reference level {BitConverter.ToString(value.ToArray())}");

      int tenths =
        wholeDb * 10 +
        decimalHigh;

      if (tenths > 200)
        throw new InvalidReplyException(
          $"Invalid IC-9700 scope reference magnitude {tenths / 10.0:0.0} dB");

      return value[2] == 1
        ? -tenths
        : tenths;
    }

    public static IcomScopeSweepSpeed DecodeSpeed(
      ReadOnlySpan<byte> value)
    {
      RequireLength(
        value,
        1,
        nameof(value));

      if (value[0] > 2)
        throw new InvalidReplyException(
          $"Invalid IC-9700 scope sweep speed 0x{value[0]:X2}");

      return
        (IcomScopeSweepSpeed)value[0];
    }

    public static bool TryExtractQueryReply(
      ReadOnlySpan<byte> frame,
      IcomScopeReceiver receiver,
      byte subcommand,
      int valueLength,
      out byte[] value)
    {
      value = [];

      int expectedLength =
        8 + valueLength;

      if (frame.Length != expectedLength ||
          frame[0] != 0xFE ||
          frame[1] != 0xFE ||
          frame[2] != ControllerAddress ||
          frame[3] != RadioAddress ||
          frame[4] != Command ||
          frame[5] != subcommand ||
          frame[6] != (byte)receiver ||
          frame[^1] != 0xFD)
        return false;

      value =
        frame
          .Slice(7, valueLength)
          .ToArray();

      return true;
    }

    public static byte[] BuildAckPattern() =>
      [
        0xFE, 0xFE,
        ControllerAddress,
        RadioAddress,
        0xFB,
        0xFD
      ];

    private static void ValidateReceiver(
      IcomScopeReceiver receiver)
    {
      if ((byte)receiver > 1)
        throw new ArgumentOutOfRangeException(nameof(receiver));
    }

    private static void ValidateSubcommand(
      byte subcommand)
    {
      if (subcommand is not
          (ModeSubcommand or
           SpanSubcommand or
           EdgeSubcommand or
           HoldSubcommand or
           ReferenceSubcommand or
           SpeedSubcommand))
        throw new ArgumentOutOfRangeException(nameof(subcommand));
    }

    private static byte[] EncodeBcdLittleEndian(
      long value,
      int byteCount)
    {
      if (value < 0)
        throw new ArgumentOutOfRangeException(nameof(value));

      var bytes =
        new byte[byteCount];

      for (int i = 0;
           i < byteCount;
           i++)
      {
        int low =
          (int)(value % 10);
        value /= 10;

        int high =
          (int)(value % 10);
        value /= 10;

        bytes[i] =
          (byte)((high << 4) | low);
      }

      if (value != 0)
        throw new ArgumentOutOfRangeException(nameof(value));

      return bytes;
    }

    private static long DecodeBcdLittleEndian(
      ReadOnlySpan<byte> bytes)
    {
      long value = 0;
      long multiplier = 1;

      foreach (byte b in bytes)
      {
        int low = b & 0x0F;
        int high = (b >> 4) & 0x0F;

        if (low > 9 || high > 9)
          throw new InvalidReplyException(
            $"Invalid BCD byte 0x{b:X2}");

        value +=
          low * multiplier;
        multiplier *= 10;

        value +=
          high * multiplier;
        multiplier *= 10;
      }

      return value;
    }

    private static byte ToBcdByte(int value)
    {
      if (value is < 0 or > 99)
        throw new ArgumentOutOfRangeException(nameof(value));

      return
        (byte)(((value / 10) << 4) |
               (value % 10));
    }

    private static int FromBcdByte(byte value)
    {
      int high =
        (value >> 4) & 0x0F;
      int low =
        value & 0x0F;

      if (high > 9 || low > 9)
        throw new InvalidReplyException(
          $"Invalid BCD byte 0x{value:X2}");

      return high * 10 + low;
    }

    private static void RequireLength(
      ReadOnlySpan<byte> value,
      int length,
      string paramName)
    {
      if (value.Length != length)
        throw new ArgumentException(
          $"Expected {length} byte(s), got {value.Length}.",
          paramName);
    }
  }
}
