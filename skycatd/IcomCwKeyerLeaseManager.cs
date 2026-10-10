using System.Globalization;
using Microsoft.Extensions.Logging;
using SkyCat;

namespace skycatd;

/// <summary>
/// Exclusive, fail-closed lease for IC-9700 CI-V Command 17 CW messages.
///
/// The CW lease shares transmitter ownership with PttLeaseManager: a keyer
/// SEND cannot start while CAT/WSJT-X owns PTT, and PTT cannot assert while
/// a CW lease is active. Because Command 17 has no "message finished" readback,
/// ownership is intentionally retained until STOP or client disconnect.
/// </summary>
public sealed class IcomCwKeyerLeaseManager
{
  private readonly Func<CatCommand, string?> SendCat;
  private readonly Func<int> ReadBreakIn;
  private readonly Func<int> ReadKeySpeedRaw;
  private readonly Action<int> WriteKeySpeedRaw;
  private readonly Action<string> SendCw;
  private readonly Action StopCw;
  private readonly PttLeaseManager PttLease;
  private readonly ILogger? Logger;
  private readonly object Sync = new();

  private object? Owner;
  private bool MayBeSending;
  private bool Orphaned;

  public IcomCwKeyerLeaseManager(
    CatCommandSender sender,
    PttLeaseManager pttLease,
    ILogger? logger = null)
  {
    ArgumentNullException.ThrowIfNull(sender);

    SendCat =
      command =>
        sender.SendCommand(command);
    ReadBreakIn =
      sender.ReadIcomBreakInMode;
    ReadKeySpeedRaw =
      sender.ReadIcomKeySpeedRaw;
    WriteKeySpeedRaw =
      sender.SetIcomKeySpeedRaw;
    SendCw =
      sender.SendIcomCwMessage;
    StopCw =
      sender.StopIcomCwMessage;
    PttLease = pttLease ??
      throw new ArgumentNullException(nameof(pttLease));
    Logger = logger;
  }

  public IcomCwKeyerLeaseManager(
    Func<CatCommand, string?> sendCat,
    Func<int> readBreakIn,
    Func<int> readKeySpeedRaw,
    Action<int> writeKeySpeedRaw,
    Action<string> sendCw,
    Action stopCw,
    PttLeaseManager pttLease,
    ILogger? logger = null)
  {
    SendCat = sendCat ??
      throw new ArgumentNullException(nameof(sendCat));
    ReadBreakIn = readBreakIn ??
      throw new ArgumentNullException(nameof(readBreakIn));
    ReadKeySpeedRaw = readKeySpeedRaw ??
      throw new ArgumentNullException(nameof(readKeySpeedRaw));
    WriteKeySpeedRaw = writeKeySpeedRaw ??
      throw new ArgumentNullException(nameof(writeKeySpeedRaw));
    SendCw = sendCw ??
      throw new ArgumentNullException(nameof(sendCw));
    StopCw = stopCw ??
      throw new ArgumentNullException(nameof(stopCw));
    PttLease = pttLease ??
      throw new ArgumentNullException(nameof(pttLease));
    Logger = logger;
  }

  public string Execute(
    object client,
    string request)
  {
    if (request == "PING")
      return "PONG";

    if (request == "CAPS")
      return "CAPS MAX=30 MODES=CW,CW-R BKIN=REQUIRED STOP=FF TXHZ=STATUS FREQCHECK=SENDHZ KEYSPEED=SETWPM,SETKEYRAW";

    if (request == "STATUS")
      return Status(client);

    if (request == "STOP")
      return Stop(client);

    if (request.StartsWith(
          "SETWPM ",
          StringComparison.Ordinal))
      return SetWpm(
        client,
        request[7..]);

    if (request.StartsWith(
          "SETKEYRAW ",
          StringComparison.Ordinal))
      return SetKeyRaw(
        client,
        request[10..]);

    if (request.StartsWith(
          "SENDHZ ",
          StringComparison.Ordinal))
      return SendChecked(
        client,
        request[7..]);

    if (request.StartsWith(
          "SEND ",
          StringComparison.Ordinal))
      return Send(
        client,
        request[5..],
        expectedTxHz: null,
        toleranceHz: 0);

    return "ERR INVALID";
  }

  private string Status(object client)
  {
    lock (Sync)
    {
      string lease =
        Owner == null
          ? "IDLE"
          : ReferenceEquals(
              Owner,
              client)
            ? Orphaned
              ? "UNCERTAIN"
              : "OWNED"
            : "BUSY";

      try
      {
        string mode =
          SendCat(
            CatCommand.read_tx_mode)
          ?? "?";

        int breakIn =
          ReadBreakIn();

        int keyRaw =
          ReadKeySpeedRaw();

        string tx =
          SendCat(
            CatCommand.read_ptt)
          ?? "?";

        long txHz =
          ReadActualTxFrequencyHz();

        return
          $"STATUS {lease} MODE={mode} BKIN={breakIn} TX={tx} KEYRAW={keyRaw} TXHZ={txHz.ToString(CultureInfo.InvariantCulture)}";
      }
      catch (Exception ex)
      {
        return FormatError(ex);
      }
    }
  }

  private string SetWpm(
    object client,
    string argument)
  {
    if (!double.TryParse(
          argument,
          NumberStyles.AllowDecimalPoint,
          CultureInfo.InvariantCulture,
          out double wpm) ||
        !double.IsFinite(wpm) ||
        wpm is < 6.0 or > 48.0)
      return "ERR INVALID";

    int raw = (int)Math.Round(
      (wpm - 6.0) * 255.0 / 42.0,
      MidpointRounding.AwayFromZero);

    return SetKeySpeedRaw(client, raw);
  }

  private string SetKeyRaw(
    object client,
    string argument)
  {
    if (!int.TryParse(
          argument,
          NumberStyles.None,
          CultureInfo.InvariantCulture,
          out int raw) ||
        raw is < 0 or > 255)
      return "ERR INVALID";

    return SetKeySpeedRaw(client, raw);
  }

  private string SetKeySpeedRaw(
    object client,
    int raw)
  {
    lock (Sync)
    {
      if (Owner != null)
        return "ERR BUSY";

      try
      {
        string tx =
          SendCat(
            CatCommand.read_ptt)
          ?? "1";

        if (tx != "0")
          return "ERR TXACTIVE";

        WriteKeySpeedRaw(raw);
        int verifiedRaw =
          ReadKeySpeedRaw();

        if (verifiedRaw != raw)
          return "ERR VERIFY";

        double verifiedWpm =
          6.0 +
          verifiedRaw * 42.0 / 255.0;

        return
          $"OK KEYRAW={verifiedRaw} WPM={verifiedWpm.ToString("F2", CultureInfo.InvariantCulture)}";
      }
      catch (Exception ex)
      {
        return FormatError(ex);
      }
    }
  }

  private string SendChecked(
    object client,
    string arguments)
  {
    string[] parts =
      arguments.Split(
        ' ',
        3,
        StringSplitOptions.RemoveEmptyEntries);

    if (parts.Length != 3 ||
        !long.TryParse(
          parts[0],
          NumberStyles.None,
          CultureInfo.InvariantCulture,
          out long expectedTxHz) ||
        !int.TryParse(
          parts[1],
          NumberStyles.None,
          CultureInfo.InvariantCulture,
          out int toleranceHz) ||
        expectedTxHz <= 0 ||
        toleranceHz is < 0 or > 5000)
      return "ERR INVALID";

    return Send(
      client,
      parts[2],
      expectedTxHz,
      toleranceHz);
  }

  private string Send(
    object client,
    string text,
    long? expectedTxHz,
    int toleranceHz)
  {
    try
    {
      IcomCwMessageCodec.Validate(text);
    }
    catch (ArgumentException)
    {
      return "ERR INVALID";
    }

    lock (Sync)
    {
      if (Owner != null &&
          Orphaned)
        TryFailSafeStop();

      if (Owner != null)
        return "ERR BUSY";

      if (!PttLease.TryAcquireExternal(
            client))
        return "ERR BUSY";

      bool reservationHeld = true;

      try
      {
        string mode =
          SendCat(
            CatCommand.read_tx_mode)
          ?? string.Empty;

        if (mode is not ("CW" or "CW-R"))
        {
          PttLease.ReleaseExternal(client);
          reservationHeld = false;
          return "ERR MODE";
        }

        int breakIn =
          ReadBreakIn();

        if (breakIn == 0)
        {
          PttLease.ReleaseExternal(client);
          reservationHeld = false;
          return "ERR BREAKIN";
        }

        string ptt =
          SendCat(
            CatCommand.read_ptt)
          ?? "1";

        // Do not inject a CW message into a transmitter that is already keyed
        // by the front panel, an external line, or an untracked controller.
        if (ptt != "0")
        {
          PttLease.ReleaseExternal(client);
          reservationHeld = false;
          return "ERR TXACTIVE";
        }

        if (expectedTxHz.HasValue)
        {
          long actualTxHz =
            ReadActualTxFrequencyHz();

          if (Math.Abs(
                actualTxHz -
                expectedTxHz.Value) >
              toleranceHz)
          {
            PttLease.ReleaseExternal(client);
            reservationHeld = false;
            return "ERR FREQ";
          }
        }

        // Reserve ownership before the write: a timeout after SerialPort.Write
        // is ambiguous and the radio may already be sending CW.
        Owner = client;
        MayBeSending = true;
        Orphaned = false;

        SendCw(text);
        return "OK";
      }
      catch (Exception ex)
      {
        if (Owner != null &&
            ReferenceEquals(
              Owner,
              client))
          TryFailSafeStop();
        else if (reservationHeld)
          PttLease.ReleaseExternal(client);

        return FormatError(ex);
      }
    }
  }

  private long ReadActualTxFrequencyHz()
  {
    string? value =
      SendCat(
        CatCommand.read_tx_frequency);

    if (!long.TryParse(
          value,
          NumberStyles.None,
          CultureInfo.InvariantCulture,
          out long frequencyHz) ||
        frequencyHz <= 0)
      throw new InvalidReplyException(
        "IC-9700 returned an invalid TX frequency.");

    return frequencyHz;
  }

  private string Stop(object client)
  {
    lock (Sync)
    {
      if (Owner == null)
        return "OK";

      if (!ReferenceEquals(
            Owner,
            client))
        return "ERR BUSY";

      try
      {
        StopCw();
        ClearOwner(client);
        return "OK";
      }
      catch (Exception ex)
      {
        Orphaned = true;
        return FormatError(ex);
      }
    }
  }

  public void Release(object client)
  {
    lock (Sync)
    {
      if (!ReferenceEquals(
            Owner,
            client))
      {
        PttLease.ReleaseExternal(client);
        return;
      }

      TryFailSafeStop();
      if (Owner != null)
        Orphaned = true;
    }
  }

  /// <summary>
  /// Called after a serial reconnect and during shutdown. An uncertain CW
  /// lease remains fail-closed until binary Command 17 FF is acknowledged.
  /// </summary>
  public void RetryUncertainRelease()
  {
    lock (Sync)
    {
      if (Owner != null &&
          MayBeSending)
        TryFailSafeStop();
    }
  }

  public bool HasLease
  {
    get
    {
      lock (Sync)
        return Owner != null ||
               MayBeSending;
    }
  }

  private void TryFailSafeStop()
  {
    object? owner = Owner;

    if (owner == null)
      return;

    try
    {
      StopCw();
      ClearOwner(owner);
    }
    catch (Exception ex)
    {
      Orphaned = true;
      Logger?.LogWarning(
        "CW fail-safe STOP could not be confirmed: {Error}",
        ex.Message);
    }
  }

  private void ClearOwner(object client)
  {
    if (!ReferenceEquals(
          Owner,
          client))
      return;

    Owner = null;
    MayBeSending = false;
    Orphaned = false;
    PttLease.ReleaseExternal(client);
  }

  private static string FormatError(
    Exception ex) =>
    ex switch
    {
      NotSupportedException =>
        "ERR UNSUPPORTED",
      InvalidOperationException =>
        "ERR DISCONNECTED",
      TimeoutException =>
        "ERR TIMEOUT",
      InvalidReplyException =>
        "ERR REJECTED",
      ArgumentException =>
        "ERR INVALID",
      _ =>
        "ERR RADIO"
    };
}
