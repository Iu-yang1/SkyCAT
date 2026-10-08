namespace SkyCat
{
  public enum Icom9700ScopeReceiver : byte
  {
    Main = 0x00,
    Sub = 0x01
  }

  public enum Icom9700ScopeMode : byte
  {
    Center = 0x00,
    Fixed = 0x01,
    ScrollCenter = 0x02,
    ScrollFixed = 0x03
  }

  public enum Icom9700ScopeSweepSpeed : byte
  {
    Fast = 0x00,
    Mid = 0x01,
    Slow = 0x02
  }

  public enum Icom9700ScopeCenterType : byte
  {
    FilterCenter = 0x00,
    CarrierPoint = 0x01,
    CarrierPointAbsolute = 0x02
  }

  public enum Icom9700ScopeVbw : byte
  {
    Narrow = 0x00,
    Wide = 0x01
  }

  public enum Icom9700ScopeMarkerPosition : byte
  {
    FilterCenter = 0x00,
    CarrierPoint = 0x01
  }

  public sealed class Icom9700ScopeSnapshot
  {
    public Icom9700ScopeReceiver SelectedScope { get; init; }

    public Icom9700ScopeMode MainMode { get; init; }
    public long MainSpanHz { get; init; }
    public int MainEdge { get; init; }
    public double MainReferenceDb { get; init; }
    public Icom9700ScopeSweepSpeed MainSpeed { get; init; }
    public Icom9700ScopeVbw MainVbw { get; init; }

    public Icom9700ScopeMode SubMode { get; init; }
    public long SubSpanHz { get; init; }
    public int SubEdge { get; init; }
    public double SubReferenceDb { get; init; }
    public Icom9700ScopeSweepSpeed SubSpeed { get; init; }
    public Icom9700ScopeVbw SubVbw { get; init; }

    public bool ScopeDuringTx { get; init; }
    public Icom9700ScopeCenterType CenterType { get; init; }
    public Icom9700ScopeMarkerPosition MarkerPosition { get; init; }

    public string ToProtocolString() =>
      string.Join(
        ';',
        $"SELECT={FormatReceiver(SelectedScope)}",
        $"MAIN.MODE={FormatMode(MainMode)}",
        $"MAIN.SPAN={MainSpanHz}",
        $"MAIN.EDGE={MainEdge}",
        $"MAIN.REF={MainReferenceDb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}",
        $"MAIN.SPEED={MainSpeed.ToString().ToUpperInvariant()}",
        $"MAIN.VBW={MainVbw.ToString().ToUpperInvariant()}",
        $"SUB.MODE={FormatMode(SubMode)}",
        $"SUB.SPAN={SubSpanHz}",
        $"SUB.EDGE={SubEdge}",
        $"SUB.REF={SubReferenceDb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}",
        $"SUB.SPEED={SubSpeed.ToString().ToUpperInvariant()}",
        $"SUB.VBW={SubVbw.ToString().ToUpperInvariant()}",
        $"TX={(ScopeDuringTx ? 1 : 0)}",
        $"CENTER={FormatCenterType(CenterType)}",
        $"MARKER={(MarkerPosition == Icom9700ScopeMarkerPosition.CarrierPoint ? "CARRIER" : "FILTER")}");

    private static string FormatReceiver(
      Icom9700ScopeReceiver receiver) =>
      receiver ==
        Icom9700ScopeReceiver.Sub
          ? "SUB"
          : "MAIN";

    private static string FormatMode(
      Icom9700ScopeMode mode) =>
      mode switch
      {
        Icom9700ScopeMode.Center =>
          "CENTER",
        Icom9700ScopeMode.Fixed =>
          "FIXED",
        Icom9700ScopeMode.ScrollCenter =>
          "SCROLL-C",
        Icom9700ScopeMode.ScrollFixed =>
          "SCROLL-F",
        _ =>
          throw new ArgumentOutOfRangeException(
            nameof(mode))
      };

    private static string FormatCenterType(
      Icom9700ScopeCenterType type) =>
      type switch
      {
        Icom9700ScopeCenterType.FilterCenter =>
          "FILTER",
        Icom9700ScopeCenterType.CarrierPoint =>
          "CARRIER",
        Icom9700ScopeCenterType.CarrierPointAbsolute =>
          "ABS",
        _ =>
          throw new ArgumentOutOfRangeException(
            nameof(type))
      };
  }

  /// <summary>
  /// Pure builders for IC-9700 CI-V spectrum-scope commands.
  /// Keeping wire encoding out of the daemon command parser makes the byte
  /// contract directly unit-testable without opening a serial port.
  /// </summary>
  public static class Icom9700ScopeCommands
  {
    private static readonly HashSet<long> SupportedSpansHz =
    [
      2_500,
      5_000,
      10_000,
      25_000,
      50_000,
      100_000,
      250_000,
      500_000
    ];

    public static byte[] BuildSelectedScope(
      Icom9700ScopeReceiver receiver) =>
      Build(
        0x12,
        (byte)receiver);

    public static byte[] BuildMode(
      Icom9700ScopeReceiver receiver,
      Icom9700ScopeMode mode) =>
      Build(
        0x14,
        (byte)receiver,
        (byte)mode);

    public static byte[] BuildSpan(
      Icom9700ScopeReceiver receiver,
      long spanHz)
    {
      if (!SupportedSpansHz.Contains(spanHz))
        throw new ArgumentOutOfRangeException(
          nameof(spanHz),
          spanHz,
          "IC-9700 scope span must be one of 2.5, 5, 10, 25, 50, 100, 250 or 500 kHz.");

      return Build(
        0x15,
        [(byte)receiver, .. EncodeFrequencyBcdLe(spanHz)]);
    }

    public static byte[] BuildEdge(
      Icom9700ScopeReceiver receiver,
      int edgeNumber)
    {
      if (edgeNumber is < 1 or > 4)
        throw new ArgumentOutOfRangeException(
          nameof(edgeNumber),
          edgeNumber,
          "IC-9700 scope edge number must be 1 through 4.");

      return Build(
        0x16,
        (byte)receiver,
        (byte)edgeNumber);
    }

    public static byte[] BuildReferenceLevel(
      Icom9700ScopeReceiver receiver,
      double referenceDb)
    {
      if (!double.IsFinite(referenceDb) ||
          referenceDb < -20.0 ||
          referenceDb > 20.0)
        throw new ArgumentOutOfRangeException(
          nameof(referenceDb),
          referenceDb,
          "IC-9700 scope reference level must be between -20.0 and +20.0 dB.");

      double tenthsExact =
        Math.Abs(referenceDb) * 10.0;
      int tenths =
        checked((int)Math.Round(
          tenthsExact,
          MidpointRounding.AwayFromZero));

      if (Math.Abs(tenthsExact - tenths) > 1e-9 ||
          tenths % 5 != 0)
        throw new ArgumentOutOfRangeException(
          nameof(referenceDb),
          referenceDb,
          "IC-9700 scope reference level uses 0.5 dB steps.");

      int wholeDb =
        tenths / 10;
      int fractionalTenths =
        tenths % 10;

      byte wholeBcd =
        (byte)(
          ((wholeDb / 10) << 4) |
          (wholeDb % 10));

      byte fractionalBcd =
        (byte)(
          fractionalTenths << 4);

      byte sign =
        referenceDb < 0
          ? (byte)0x01
          : (byte)0x00;

      return Build(
        0x19,
        (byte)receiver,
        wholeBcd,
        fractionalBcd,
        sign);
    }

    public static byte[] BuildSweepSpeed(
      Icom9700ScopeReceiver receiver,
      Icom9700ScopeSweepSpeed speed) =>
      Build(
        0x1A,
        (byte)receiver,
        (byte)speed);

    public static byte[] BuildScopeDuringTx(
      bool enabled) =>
      Build(
        0x1B,
        enabled
          ? (byte)0x01
          : (byte)0x00);

    public static byte[] BuildCenterType(
      Icom9700ScopeCenterType type) =>
      Build(
        0x1C,
        (byte)type);

    public static byte[] BuildVbw(
      Icom9700ScopeReceiver receiver,
      Icom9700ScopeVbw vbw) =>
      Build(
        0x1D,
        (byte)receiver,
        (byte)vbw);

    public static byte[] BuildMarkerPosition(
      Icom9700ScopeMarkerPosition position) =>
      Build(
        0x20,
        (byte)position);

    public static byte[] BuildFixedEdge(
      int frequencyRange,
      int edgeNumber,
      long lowerHz,
      long upperHz)
    {
      if (frequencyRange is < 1 or > 3)
        throw new ArgumentOutOfRangeException(
          nameof(frequencyRange),
          frequencyRange,
          "IC-9700 fixed-edge frequency range must be 1 (144 MHz), 2 (430 MHz), or 3 (1.2 GHz).");

      if (edgeNumber is < 1 or > 4)
        throw new ArgumentOutOfRangeException(
          nameof(edgeNumber),
          edgeNumber,
          "IC-9700 scope edge number must be 1 through 4.");

      (long minimum, long maximum) =
        frequencyRange switch
        {
          1 => (144_000_000, 148_000_000),
          2 => (430_000_000, 450_000_000),
          _ => (1_240_000_000, 1_300_000_000)
        };

      if (lowerHz < minimum ||
          lowerHz > maximum)
        throw new ArgumentOutOfRangeException(
          nameof(lowerHz));

      if (upperHz < minimum ||
          upperHz > maximum ||
          upperHz <= lowerHz)
        throw new ArgumentOutOfRangeException(
          nameof(upperHz));

      // The IC-9700 explicitly ignores digits below 1 kHz for 27 1E.
      // Reject them here instead of silently programming a different edge.
      if (lowerHz % 1_000 != 0 ||
          upperHz % 1_000 != 0)
        throw new ArgumentException(
          "IC-9700 fixed scope edges must use whole-kHz frequencies.");

      return Build(
        0x1E,
        [
          (byte)frequencyRange,
          (byte)edgeNumber,
          .. EncodeFrequencyBcdLe(lowerHz),
          .. EncodeFrequencyBcdLe(upperHz)
        ]);
    }

    public static byte[] BuildQuery(
      byte subCommand,
      params byte[] selector) =>
      Build(
        subCommand,
        selector);

    public static long DecodeFrequencyBcdLe(
      ReadOnlySpan<byte> bytes)
    {
      if (bytes.Length != 5)
        throw new ArgumentException(
          "IC-9700 scope frequency requires exactly five BCD bytes.",
          nameof(bytes));

      long value = 0;
      long multiplier = 1;

      for (int i = 0;
           i < bytes.Length;
           i++)
      {
        int low =
          bytes[i] & 0x0F;
        int high =
          (bytes[i] >> 4) & 0x0F;

        if (low > 9 ||
            high > 9)
          throw new FormatException(
            "Invalid BCD frequency returned by IC-9700.");

        value +=
          (low +
           high * 10L) *
          multiplier;
        multiplier *= 100;
      }

      return value;
    }

    public static double DecodeReferenceLevel(
      ReadOnlySpan<byte> bytes)
    {
      if (bytes.Length != 3)
        throw new ArgumentException(
          "IC-9700 scope reference level requires three bytes.",
          nameof(bytes));

      int tens =
        (bytes[0] >> 4) & 0x0F;
      int ones =
        bytes[0] & 0x0F;
      int tenths =
        (bytes[1] >> 4) & 0x0F;

      if (tens > 9 ||
          ones > 9 ||
          tenths > 9 ||
          bytes[2] > 1)
        throw new FormatException(
          "Invalid IC-9700 scope reference-level reply.");

      double value =
        tens * 10 +
        ones +
        tenths / 10.0;

      return bytes[2] == 1
        ? -value
        : value;
    }

    private static byte[] Build(
      byte subCommand,
      params byte[] data)
    {
      var frame =
        new byte[
          7 +
          data.Length];

      frame[0] = 0xFE;
      frame[1] = 0xFE;
      frame[2] = 0xA2;
      frame[3] = 0xE0;
      frame[4] = 0x27;
      frame[5] = subCommand;

      if (data.Length > 0)
        Buffer.BlockCopy(
          data,
          0,
          frame,
          6,
          data.Length);

      frame[^1] = 0xFD;
      return frame;
    }

    private static byte[] EncodeFrequencyBcdLe(
      long value)
    {
      if (value is < 0 or > 9_999_999_999)
        throw new ArgumentOutOfRangeException(
          nameof(value));

      var bytes =
        new byte[5];

      for (int i = 0;
           i < bytes.Length;
           i++)
      {
        int pair =
          (int)(value % 100);

        bytes[i] =
          (byte)(
            ((pair / 10) << 4) |
            (pair % 10));

        value /= 100;
      }

      return bytes;
    }
  }
}
