using System.IO.Ports;
using System.Text;
using Microsoft.Extensions.Logging;
using static SkyCat.CatCommandSet;

namespace SkyCat
{
  public class InvalidReplyException : Exception
  {
    public InvalidReplyException(string message) : base(message) { }
  }

  public class CatCommandSender
  {
    private readonly Dictionary<string, CatCommandSet> CommandSets = new();
    public CatCommandSet CommandSet;
    private CatCommandGroup? CommandGroup;
    private CatCommand[] AvailableWhenReceiving = [];
    private CatCommand[] AvailableWhenTransmitting = [];
    public readonly SerialPort SerialPort = new();
    public ILogger? Log;
    private OperatingMode OperatingMode;
    private readonly ScopeFrameTap ScopeTap;

    public event Action<byte[]>? ScopeFrameReceived;

    public string[] RadioNames => CommandSets.Keys.ToArray();
    public string? RadioName {get; private set; }
    public bool Transmitting {get; private set; }

    // Observed PTT state is not ownership: read_ptt may report a transmitter
    // keyed from the front panel or another controller. Track only PTT that
    // SkyCAT itself requested so shutdown never unkeys an unrelated transmission.
    private bool PttOwnedByCommand;
    private bool PttOnAttempted;
    public bool PttMayBeOwnedByCommand => PttOwnedByCommand || PttOnAttempted;
    


    public CatCommandSender(ILogger? log = null)
    {
      Log = log;
      ScopeTap = new ScopeFrameTap(frame => ScopeFrameReceived?.Invoke(frame));

      // Scope output can exceed the default System.IO.Ports receive buffer between
      // Doppler/CAT transactions. Keep enough headroom for several complete IC-9700
      // sweeps so the virtual COM driver does not drop sequence chunks.
      SerialPort.ReadBufferSize = 64 * 1024;

      string pathToCommandSets = Path.Combine(AppContext.BaseDirectory, "Rigs");
      ReadCommandSets(pathToCommandSets);
    }

    private void ReadCommandSets(string pathToCommandSets)
    {
      var fileNames = Directory.GetFiles(pathToCommandSets, "*.json");

      foreach (var fileName in fileNames)
        try
        {
          string name = Path.GetFileNameWithoutExtension(fileName);
          string json = File.ReadAllText(fileName);
          var commandSet = FromJson(json);
          CommandSets.Add(name, commandSet);
        }
        catch (Exception ex)
        {
          Log?.LogCritical(ex, $"Error loading command set '{fileName}'");
          throw;
        }

      if (CommandSets.Count == 0) throw new Exception($"No valid command sets found in {pathToCommandSets}");
    }

    public Dictionary<int, string> GetModels()
    {
      return CommandSets.Select(kv => new { kv.Value.Id, Name = kv.Key }).ToDictionary(x => x.Id, x => x.Name);
    }

    public string GetRadioName(string radioId)
    {
      if (CommandSets.Keys.Contains(radioId)) return radioId;

      return CommandSets.FirstOrDefault(kv => kv.Value.Id.ToString() == radioId).Key
        ?? throw new ArgumentException($"Command set for radio model {radioId} is not available");
    }

    public bool IsCommandAvailable(CatCommand command)
    {
      var availableCommands = Transmitting ? AvailableWhenTransmitting : AvailableWhenReceiving;
      return availableCommands.Contains(command);
    }

    public void SelectRadio(string model)
    {
      RadioName = GetRadioName(model);

      string message = $"Radio model: '{RadioName}'";
      if (RadioName != model) message += $" ({model})";
      Log?.LogInformation(message);

      // radio name
      if (string.IsNullOrEmpty(RadioName)) throw new ArgumentException("radio name is blank");
      RadioName = RadioName;

      // radio's command set
      if (!CommandSets.ContainsKey(RadioName)) throw new ArgumentException($"Command set for '{RadioName}' is not available");
      CommandSet = CommandSets[RadioName];
    }

    public void SetupRadio(OperatingMode operatingMode)
    {
      OperatingMode = operatingMode;
      Log?.LogInformation($"Setting up radio '{RadioName}' ({operatingMode})");

      // radio's commands for the given radio type
      CommandGroup = (CatCommandGroup?)CommandSet!.GetType().GetProperty($"{operatingMode}")!.GetValue(CommandSet, null);
      if (CommandGroup == null) throw new ArgumentException($"Radio '{RadioName}' has no {operatingMode} mode");
      ListAvailableCommands();

      // set up radio
      SendCommand(CatCommand.setup, null, true);
    }

    private void ListAvailableCommands()
    {
      AvailableWhenReceiving = CommandGroup!.Where(kv => kv.Value != null && kv.Value.Restriction != CatRestriction.when_transmitting).Select(kv => kv.Key).ToArray();
      AvailableWhenTransmitting = CommandGroup!.Where(kv => kv.Value != null && kv.Value.Restriction != CatRestriction.when_receiving).Select(kv => kv.Key).ToArray();
    }

    public string ListModels()
    {
      var modelList = new List<string>();
      modelList.Add("Rig #    Model");
      foreach (var kv in GetModels())
        modelList.Add($"{kv.Key:D4}     {kv.Value}");
      return string.Join(Environment.NewLine, modelList);
    }

    public string ListAllCapabilities()
    {
      var list = new List<string>();

      foreach (var radio in RadioNames)
        list.Add(ListCapabilities(radio));

      return $"[{string.Join(",\n", list)}]";
    }

    public string ListCapabilities(string? model = null)
    {
      model ??= RadioName;
      return RadioCapabilities.FromCatCommandSet(model!, CommandSets[model!]).ToJson();
    }



    public void SetIcomSelectedScope(
      Icom9700ScopeReceiver receiver) =>
      SendIcom9700ScopeCommand(
        Icom9700ScopeCommands.BuildSelectedScope(
          receiver),
        $"selected scope {receiver}");

    public void SetIcomScopeMode(
      Icom9700ScopeReceiver receiver,
      Icom9700ScopeMode mode) =>
      SendIcom9700ScopeCommand(
        Icom9700ScopeCommands.BuildMode(
          receiver,
          mode),
        $"scope {receiver} mode {mode}");

    public void SetIcomScopeSpan(
      Icom9700ScopeReceiver receiver,
      long spanHz) =>
      SendIcom9700ScopeCommand(
        Icom9700ScopeCommands.BuildSpan(
          receiver,
          spanHz),
        $"scope {receiver} span {spanHz} Hz");

    public void SetIcomScopeEdge(
      Icom9700ScopeReceiver receiver,
      int edgeNumber) =>
      SendIcom9700ScopeCommand(
        Icom9700ScopeCommands.BuildEdge(
          receiver,
          edgeNumber),
        $"scope {receiver} edge {edgeNumber}");

    public void SetIcomScopeReferenceLevel(
      Icom9700ScopeReceiver receiver,
      double referenceDb) =>
      SendIcom9700ScopeCommand(
        Icom9700ScopeCommands.BuildReferenceLevel(
          receiver,
          referenceDb),
        $"scope {receiver} reference {referenceDb:0.0} dB");

    public void SetIcomScopeSweepSpeed(
      Icom9700ScopeReceiver receiver,
      Icom9700ScopeSweepSpeed speed) =>
      SendIcom9700ScopeCommand(
        Icom9700ScopeCommands.BuildSweepSpeed(
          receiver,
          speed),
        $"scope {receiver} sweep speed {speed}");

    public void SetIcomScopeDuringTx(
      bool enabled) =>
      SendIcom9700ScopeCommand(
        Icom9700ScopeCommands.BuildScopeDuringTx(
          enabled),
        $"scope during TX {(enabled ? "on" : "off")}");

    public void SetIcomScopeCenterType(
      Icom9700ScopeCenterType type) =>
      SendIcom9700ScopeCommand(
        Icom9700ScopeCommands.BuildCenterType(
          type),
        $"scope CENTER display {type}");

    public void SetIcomScopeVbw(
      Icom9700ScopeReceiver receiver,
      Icom9700ScopeVbw vbw) =>
      SendIcom9700ScopeCommand(
        Icom9700ScopeCommands.BuildVbw(
          receiver,
          vbw),
        $"scope {receiver} VBW {vbw}");

    public void SetIcomScopeMarkerPosition(
      Icom9700ScopeMarkerPosition position) =>
      SendIcom9700ScopeCommand(
        Icom9700ScopeCommands.BuildMarkerPosition(
          position),
        $"scope marker position {position}");

    public void SetIcomScopeFixedEdge(
      int frequencyRange,
      int edgeNumber,
      long lowerHz,
      long upperHz) =>
      SendIcom9700ScopeCommand(
        Icom9700ScopeCommands.BuildFixedEdge(
          frequencyRange,
          edgeNumber,
          lowerHz,
          upperHz),
        $"scope fixed edge range {frequencyRange} edge {edgeNumber}: {lowerHz}-{upperHz} Hz");

    public Icom9700ScopeSnapshot ReadIcomScopeSnapshot()
    {
      byte[] selected =
        ReadIcom9700ScopeData(
          0x12);

      byte[] mainMode =
        ReadIcom9700ScopeData(
          0x14,
          (byte)Icom9700ScopeReceiver.Main);
      byte[] mainSpan =
        ReadIcom9700ScopeData(
          0x15,
          (byte)Icom9700ScopeReceiver.Main);
      byte[] mainEdge =
        ReadIcom9700ScopeData(
          0x16,
          (byte)Icom9700ScopeReceiver.Main);
      byte[] mainRef =
        ReadIcom9700ScopeData(
          0x19,
          (byte)Icom9700ScopeReceiver.Main);
      byte[] mainSpeed =
        ReadIcom9700ScopeData(
          0x1A,
          (byte)Icom9700ScopeReceiver.Main);
      byte[] mainVbw =
        ReadIcom9700ScopeData(
          0x1D,
          (byte)Icom9700ScopeReceiver.Main);

      byte[] subMode =
        ReadIcom9700ScopeData(
          0x14,
          (byte)Icom9700ScopeReceiver.Sub);
      byte[] subSpan =
        ReadIcom9700ScopeData(
          0x15,
          (byte)Icom9700ScopeReceiver.Sub);
      byte[] subEdge =
        ReadIcom9700ScopeData(
          0x16,
          (byte)Icom9700ScopeReceiver.Sub);
      byte[] subRef =
        ReadIcom9700ScopeData(
          0x19,
          (byte)Icom9700ScopeReceiver.Sub);
      byte[] subSpeed =
        ReadIcom9700ScopeData(
          0x1A,
          (byte)Icom9700ScopeReceiver.Sub);
      byte[] subVbw =
        ReadIcom9700ScopeData(
          0x1D,
          (byte)Icom9700ScopeReceiver.Sub);

      byte[] duringTx =
        ReadIcom9700ScopeData(
          0x1B);
      byte[] centerType =
        ReadIcom9700ScopeData(
          0x1C);
      byte[] marker =
        ReadIcom9700ScopeData(
          0x20);

      return new Icom9700ScopeSnapshot
      {
        SelectedScope =
          ParseEnumByte<Icom9700ScopeReceiver>(
            selected,
            0,
            "selected scope"),

        MainMode =
          ParseScopedEnum<Icom9700ScopeMode>(
            mainMode,
            Icom9700ScopeReceiver.Main,
            "MAIN mode"),
        MainSpanHz =
          ParseScopedSpan(
            mainSpan,
            Icom9700ScopeReceiver.Main),
        MainEdge =
          ParseScopedInteger(
            mainEdge,
            Icom9700ScopeReceiver.Main,
            1,
            4,
            "MAIN edge"),
        MainReferenceDb =
          ParseScopedReference(
            mainRef,
            Icom9700ScopeReceiver.Main),
        MainSpeed =
          ParseScopedEnum<Icom9700ScopeSweepSpeed>(
            mainSpeed,
            Icom9700ScopeReceiver.Main,
            "MAIN sweep speed"),
        MainVbw =
          ParseScopedEnum<Icom9700ScopeVbw>(
            mainVbw,
            Icom9700ScopeReceiver.Main,
            "MAIN VBW"),

        SubMode =
          ParseScopedEnum<Icom9700ScopeMode>(
            subMode,
            Icom9700ScopeReceiver.Sub,
            "SUB mode"),
        SubSpanHz =
          ParseScopedSpan(
            subSpan,
            Icom9700ScopeReceiver.Sub),
        SubEdge =
          ParseScopedInteger(
            subEdge,
            Icom9700ScopeReceiver.Sub,
            1,
            4,
            "SUB edge"),
        SubReferenceDb =
          ParseScopedReference(
            subRef,
            Icom9700ScopeReceiver.Sub),
        SubSpeed =
          ParseScopedEnum<Icom9700ScopeSweepSpeed>(
            subSpeed,
            Icom9700ScopeReceiver.Sub,
            "SUB sweep speed"),
        SubVbw =
          ParseScopedEnum<Icom9700ScopeVbw>(
            subVbw,
            Icom9700ScopeReceiver.Sub,
            "SUB VBW"),

        ScopeDuringTx =
          ParseBooleanByte(
            duringTx,
            0,
            "scope during TX"),
        CenterType =
          ParseEnumByte<Icom9700ScopeCenterType>(
            centerType,
            0,
            "CENTER type"),
        MarkerPosition =
          ParseEnumByte<Icom9700ScopeMarkerPosition>(
            marker,
            0,
            "marker position")
      };
    }

    public void SetIcomScopeSweepFast()
    {
      // Preserve the original extension contract: FAST applies to both MAIN
      // and SUB so a later scope-band switch cannot inherit a slow setting.
      SetIcomScopeSweepSpeed(
        Icom9700ScopeReceiver.Main,
        Icom9700ScopeSweepSpeed.Fast);
      SetIcomScopeSweepSpeed(
        Icom9700ScopeReceiver.Sub,
        Icom9700ScopeSweepSpeed.Fast);
    }

    private byte[] ReadIcom9700ScopeData(
      byte subCommand,
      params byte[] selector)
    {
      EnsureIcom9700ScopeReady();

      byte[] command =
        Icom9700ScopeCommands.BuildQuery(
          subCommand,
          selector);

      DumpUnexpectedBytes();
      SerialPort.Write(
        command,
        0,
        command.Length);
      SkipEcho(
        command);

      const int totalTimeoutMs = 1500;
      long deadline =
        Environment.TickCount64 +
        totalTimeoutMs;

      while (Environment.TickCount64 <
             deadline)
      {
        int remaining =
          (int)Math.Max(
            1,
            deadline -
            Environment.TickCount64);

        byte[]? frame =
          ReceiveCivFrame(
            remaining);

        if (frame == null)
          break;

        if (frame.Length == 6 &&
            frame[0] == 0xFE &&
            frame[1] == 0xFE &&
            frame[4] == 0xFA &&
            frame[5] == 0xFD)
          throw new InvalidReplyException(
            $"IC-9700 rejected scope read 27 {subCommand:X2}");

        if (frame.Length >= 7 &&
            frame[0] == 0xFE &&
            frame[1] == 0xFE &&
            frame[2] == 0xE0 &&
            frame[3] == 0xA2 &&
            frame[4] == 0x27 &&
            frame[5] == subCommand &&
            frame[^1] == 0xFD)
        {
          byte[] data =
            frame
              .Skip(6)
              .Take(
                frame.Length -
                7)
              .ToArray();

          if (selector.Length == 0 ||
              (data.Length >= selector.Length &&
               data
                 .Take(selector.Length)
                 .SequenceEqual(selector)))
            return data;
        }

        if (IsScopeWaveformFrame(
              frame))
          continue;

        Log?.LogTrace(
          $"Ignoring unrelated CI-V frame while reading scope state: {BitConverter.ToString(frame)}");
      }

      throw new TimeoutException(
        $"Timed out reading IC-9700 scope command 27 {subCommand:X2}");
    }

    private void EnsureIcom9700ScopeReady()
    {
      EnsureIcom9700ScopeReady();
    }

    private static TEnum ParseEnumByte<TEnum>(
      byte[] data,
      int index,
      string name)
      where TEnum : struct, Enum
    {
      if (index < 0 ||
          index >= data.Length ||
          !Enum.IsDefined(
            typeof(TEnum),
            (int)data[index]))
        throw new FormatException(
          $"Invalid {name} returned by IC-9700.");

      return (TEnum)Enum.ToObject(
        typeof(TEnum),
        data[index]);
    }

    private static TEnum ParseScopedEnum<TEnum>(
      byte[] data,
      Icom9700ScopeReceiver receiver,
      string name)
      where TEnum : struct, Enum
    {
      ValidateScopeSelector(
        data,
        receiver,
        2,
        name);

      return ParseEnumByte<TEnum>(
        data,
        1,
        name);
    }

    private static int ParseScopedInteger(
      byte[] data,
      Icom9700ScopeReceiver receiver,
      int minimum,
      int maximum,
      string name)
    {
      ValidateScopeSelector(
        data,
        receiver,
        2,
        name);

      int value =
        data[1];

      if (value < minimum ||
          value > maximum)
        throw new FormatException(
          $"Invalid {name} returned by IC-9700.");

      return value;
    }

    private static long ParseScopedSpan(
      byte[] data,
      Icom9700ScopeReceiver receiver)
    {
      ValidateScopeSelector(
        data,
        receiver,
        6,
        "scope span");

      return
        Icom9700ScopeCommands
          .DecodeFrequencyBcdLe(
            data.AsSpan(
              1,
              5));
    }

    private static double ParseScopedReference(
      byte[] data,
      Icom9700ScopeReceiver receiver)
    {
      ValidateScopeSelector(
        data,
        receiver,
        4,
        "scope reference level");

      return
        Icom9700ScopeCommands
          .DecodeReferenceLevel(
            data.AsSpan(
              1,
              3));
    }

    private static bool ParseBooleanByte(
      byte[] data,
      int index,
      string name)
    {
      if (index < 0 ||
          index >= data.Length ||
          data[index] > 1)
        throw new FormatException(
          $"Invalid {name} returned by IC-9700.");

      return data[index] == 1;
    }

    private static void ValidateScopeSelector(
      byte[] data,
      Icom9700ScopeReceiver receiver,
      int minimumLength,
      string name)
    {
      if (data.Length < minimumLength ||
          data[0] !=
            (byte)receiver)
        throw new FormatException(
          $"Invalid {name} receiver returned by IC-9700.");
    }

    private void SendIcom9700ScopeCommand(
      byte[] command,
      string comment)
    {
      if (!SerialPort.IsOpen)
        throw new InvalidOperationException(
          "Serial port is not open");

      if (!string.Equals(
            RadioName,
            "IC-9700",
            StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(
          $"Spectrum-scope control is only implemented for IC-9700, current radio is '{RadioName}'.");

      var message =
        new CatMessage
        {
          Command =
            command
              .Select(
                value =>
                  (byte?)value)
              .ToArray(),
          Reply =
          [
            0xFE,
            0xFE,
            0xE0,
            0xA2,
            0xFB,
            0xFD
          ],
          Comment = comment
        };

      _ = SendMessage(message);
    }


    //----------------------------------------------------------------------------------------------
    //                                send and receive
    //----------------------------------------------------------------------------------------------
    // send command, may require sending more than one CAT message
    public string? SendCommand(CatCommand command, string? paramValue = null, bool isSetup = false)
    {
      if (!SerialPort.IsOpen) throw new InvalidOperationException("Serial port is not open");

      var commandInfo = CommandGroup!.GetValueOrDefault(command);
      if (commandInfo == null) throw new ArgumentException($"Command {command} not is defined");

      string? returnedValue = null;

      if (command == CatCommand.write_ptt_on)
        PttOnAttempted = true;

      try
      {
        string logMessage = $"  Sending command: {command} {paramValue}";
        if (isSetup) Log?.LogInformation(logMessage); else Log?.LogDebug(logMessage);

        // Send every message in the command sequence. Keep the first parsed
        // return value, but never let a non-null query result suppress trailing
        // cleanup/focus messages (for example: read SUB, then restore MAIN).
        // If a message fails, messages explicitly marked AlwaysExecute are still
        // attempted before the original exception is rethrown.
        returnedValue = SendMessageSequence(
          commandInfo.Messages,
          paramValue,
          isSetup);

        logMessage = $"  Command {command} returned '{returnedValue ?? "OK"}'";
        if (isSetup) Log?.LogInformation(logMessage); else Log?.LogDebug(logMessage);
      }
      catch (InvalidReplyException)
      {
        if (commandInfo.AltMessages == null) throw;

        Log?.LogWarning($"Command {command} rejected by the radio. Trying alternative command.");
        returnedValue = SendMessageSequence(
          commandInfo.AltMessages,
          paramValue,
          isSetup);
        Log?.LogDebug($"Alternative command {command} returned '{returnedValue ?? "OK"}'");
      }

      // Keep observed PTT state and SkyCAT ownership separate.
      if (command == CatCommand.write_ptt_off)
      {
        Transmitting = false;
        PttOwnedByCommand = false;
        PttOnAttempted = false;
      }
      else if (command == CatCommand.write_ptt_on)
      {
        Transmitting = true;
        PttOwnedByCommand = true;
        PttOnAttempted = false;
      }
      else if (command == CatCommand.read_ptt)
      {
        Transmitting = returnedValue == "ON";

        if (!Transmitting)
        {
          // Hardware RX proves there is no SkyCAT-owned PTT left active.
          PttOwnedByCommand = false;
          PttOnAttempted = false;
        }
        else if (PttOnAttempted)
        {
          // A PTT-ON command can time out after the radio already acted on it.
          // A later ON readback confirms that our attempted key may be active.
          PttOwnedByCommand = true;
          PttOnAttempted = false;
        }

        returnedValue = Transmitting ? "1" : "0";
      }

      return returnedValue;
    }

    private string? SendMessageSequence(
      CatMessage[] messages,
      string? paramValue,
      bool isSetup)
    {
      string? returnedValue = null;

      for (int i = 0; i < messages.Length; i++)
      {
        try
        {
          string? messageValue = SendMessage(messages[i], paramValue, isSetup);
          returnedValue ??= messageValue;
        }
        catch
        {
          // A TX-side query/write may have already selected SUB before failing.
          // Do not leave the radio in that intermediate state: run only explicitly
          // designated cleanup messages and preserve the original failure.
          for (int j = i + 1; j < messages.Length; j++)
          {
            CatMessage cleanup = messages[j];
            if (!cleanup.AlwaysExecute) continue;

            try
            {
              _ = SendMessage(cleanup, paramValue, isSetup);
            }
            catch (Exception cleanupEx)
            {
              Log?.LogWarning(
                cleanupEx,
                "CAT cleanup message failed after an earlier command-sequence error: {Comment}",
                cleanup.Comment ?? "<no comment>");
            }
          }

          throw;
        }
      }

      return returnedValue;
    }

    // send CAT message
    private string? SendMessage(CatMessage message, string? paramValue = null, bool isSetup = false)
    {
      DumpUnexpectedBytes();

      byte[] commandBytes = message.Command.Select(b => b ?? 0).ToArray();

      if (message.CommandParam != null)
      {
        int byteCount = message.Command.Count(b => b == null);
        int start = Array.IndexOf(message.Command, null)!;

        byte[] paramBytes = ParamToBytes(message.CommandParam, paramValue, byteCount);
        Array.Copy(paramBytes, 0, commandBytes, start, paramBytes.Length);
      }

      string logMmessage = $"  Sending bytes: {BitConverter.ToString(commandBytes)}";
      if (message.Comment != null) logMmessage += $" ({message.Comment})";
      if (isSetup) Log?.LogInformation(logMmessage); else Log?.LogTrace(logMmessage);
      SerialPort.Write(commandBytes, 0, commandBytes.Length);

      SkipEcho(commandBytes);
      return ReceiveReply(message, isSetup);
    }

    private void SkipEcho(byte[] commandBytes)
    {
      if (!CommandSet!.Echo) return;

      byte[] receivedBytes = new byte[commandBytes.Length];
      int receivedCount = 0;

      try
      {
        receivedCount = ReceiveBytes(receivedBytes, 0, receivedBytes.Length);

        if (receivedCount < commandBytes.Length)
          throw new InvalidReplyException($"Received only {receivedCount} bytes, expected {commandBytes.Length} bytes.");

        if (!receivedBytes.SequenceEqual(commandBytes))
          throw new InvalidReplyException($"Expected {BitConverter.ToString(commandBytes)}, received {BitConverter.ToString(receivedBytes)}");
      }
      catch (Exception ex)
      {
        // forgive echo errors, log them and continue
        Log?.LogError($"Echo error: {ex.Message}");
      }
      finally
      {
        receivedBytes = receivedBytes.Take(receivedCount).ToArray();
        Log?.LogTrace($"  Received echo: {BitConverter.ToString(receivedBytes)}");
      }
    }

    private string? ReceiveReply(CatMessage message, bool isSetup)
    {
      if (message.Reply == null) return null;

      // Icom CI-V is an asynchronous framed protocol. While a command is waiting for
      // its FB/FA/query reply, the radio may emit unrelated transceive notifications or
      // high-rate 27 00 scope waveform frames. Those are not command failures and must
      // be skipped until the expected FE FE ... FD frame arrives.
      if (IsCivPattern(message.Reply))
        return ReceiveCivReply(message, isSetup);

      int replyLength = message.Reply.Length;
      int badReplyLength = CommandSet!.BadReply?.Length ?? 0;

      byte[] receivedBytes = new byte[replyLength];
      int receivedByteCount = 0;

      try
      {
        // try to read bad reply first
        if (CommandSet!.BadReply != null && badReplyLength <= replyLength)
        {
          receivedByteCount = ReceiveBytes(receivedBytes, 0, badReplyLength);
          if (receivedByteCount < badReplyLength)
            throw new TimeoutException($"Received {receivedByteCount} bytes, expected at least {badReplyLength} bytes.");

          byte[] possibleBadReply = receivedBytes.Take(badReplyLength).ToArray();

          if (BytesMatch(possibleBadReply, CommandSet.BadReply))
            throw new InvalidReplyException($"Command rejected by the radio");
        }

        // read the rest of the reply
        if (receivedByteCount < replyLength)
          receivedByteCount += ReceiveBytes(receivedBytes, badReplyLength, replyLength - badReplyLength);
        if (receivedByteCount < replyLength)
          throw new TimeoutException($"Received {receivedByteCount} bytes, expected {replyLength} bytes.");

        // unexpected reply. wait 100ms for more bytes, then throw an exception
        if (!BytesMatch(receivedBytes, message.Reply))
        {
          int extraCount = 100;
          Array.Resize(ref receivedBytes, receivedByteCount + extraCount);
          ReceiveBytes(receivedBytes, receivedByteCount, extraCount, 100 /*ms*/);
          throw new InvalidReplyException($"Reply mismatch: expected {NullableBytesToString(message.Reply)}");
        }
      }
      catch (InvalidReplyException)
      {
        if (message.IgnoreError) Log?.LogWarning("  Ignoring bad reply:");
        else throw;
      }
      finally
      {
        string logMessage = $"  Bytes received: {BitConverter.ToString(receivedBytes.Take(receivedByteCount).ToArray())}";
        if (isSetup) Log?.LogInformation(logMessage); else Log?.LogTrace(logMessage);
      }

      // parse good reply
      if (message.ReplyParam == null) return null;
      int paramStart = message.ReplyParam.Start ?? Array.IndexOf(message.Reply, null)!;
      int paramLength = message.ReplyParam.Length ?? message.Reply.Count(b => b == null);

      byte[] paramBytes = receivedBytes.Skip(paramStart).Take(paramLength).ToArray();
      if (message.ReplyParam.Mask != null) ApplyMask(paramBytes, message.ReplyParam.Mask);
      return BytesToParam(message.ReplyParam, paramBytes);
    }




    private string? ReceiveCivReply(CatMessage message, bool isSetup)
    {
      const int totalTimeoutMs = 1500;
      long deadline = Environment.TickCount64 + totalTimeoutMs;
      int ignoredFrames = 0;
      int ignoredScopeFrames = 0;
      byte[]? lastUnexpected = null;

      while (Environment.TickCount64 < deadline)
      {
        int remaining = (int)Math.Max(1, deadline - Environment.TickCount64);
        byte[]? frame = ReceiveCivFrame(remaining);
        if (frame == null) break;

        if (CommandSet!.BadReply != null &&
            BytesMatch(frame, CommandSet.BadReply))
        {
          if (message.IgnoreError)
          {
            Log?.LogDebug(
              $"  Ignoring expected CI-V command rejection: {BitConverter.ToString(frame)}");
            return null;
          }

          throw new InvalidReplyException("Command rejected by the radio");
        }

        if (BytesMatch(frame, message.Reply))
        {
          string logMessage =
            $"  Bytes received: {BitConverter.ToString(frame)}";
          if (ignoredFrames > 0)
          {
            logMessage +=
              $" (ignored {ignoredFrames} unsolicited CI-V frame(s), " +
              $"{ignoredScopeFrames} scope)";
          }

          if (isSetup) Log?.LogInformation(logMessage);
          else Log?.LogTrace(logMessage);

          if (message.ReplyParam == null) return null;

          int paramStart =
            message.ReplyParam.Start ??
            Array.IndexOf(message.Reply, null)!;
          int paramLength =
            message.ReplyParam.Length ??
            message.Reply.Count(b => b == null);

          byte[] paramBytes =
            frame.Skip(paramStart).Take(paramLength).ToArray();

          if (message.ReplyParam.Mask != null)
            ApplyMask(paramBytes, message.ReplyParam.Mask);

          return BytesToParam(message.ReplyParam, paramBytes);
        }

        ignoredFrames++;
        lastUnexpected = frame;

        if (IsScopeWaveformFrame(frame))
        {
          // The frame has already been delivered by ScopeTap because every byte read
          // by ReceiveCivFrame is tapped centrally.
          ignoredScopeFrames++;
          continue;
        }

        // Non-scope unsolicited CI-V frames are useful diagnostics, but they are still
        // not replies to the current command.
        Log?.LogTrace(
          $"  Ignoring unsolicited CI-V frame while waiting for reply: " +
          $"{BitConverter.ToString(frame)}");
      }

      string detail =
        lastUnexpected == null
          ? "no complete CI-V frame received"
          : $"last unrelated frame {BitConverter.ToString(lastUnexpected)}";

      throw new TimeoutException(
        $"Timed out waiting for {NullableBytesToString(message.Reply)}; " +
        $"ignored {ignoredFrames} unsolicited CI-V frame(s) " +
        $"({ignoredScopeFrames} scope), {detail}.");
    }

    private byte[]? ReceiveCivFrame(int timeoutMs)
    {
      const int maxFrameLength = 4096;
      long deadline = Environment.TickCount64 + Math.Max(1, timeoutMs);
      var frame = new List<byte>(128);
      bool sawFirstFe = false;
      bool inFrame = false;

      while (Environment.TickCount64 < deadline)
      {
        int remaining = (int)Math.Max(1, deadline - Environment.TickCount64);
        SerialPort.ReadTimeout = remaining;

        int value;
        try
        {
          value = SerialPort.ReadByte();
        }
        catch (TimeoutException)
        {
          return null;
        }

        if (value < 0) continue;
        byte b = (byte)value;

        // All serial receive paths feed the same scope tap. This prevents a scope
        // frame from being split across "background drain" and "command reply"
        // consumers, which previously lost one or more 01..11 scope chunks.
        ScopeTap.FeedByte(b);

        if (!inFrame)
        {
          if (!sawFirstFe)
          {
            sawFirstFe = b == 0xFE;
            continue;
          }

          if (b == 0xFE)
          {
            frame.Add(0xFE);
            frame.Add(0xFE);
            inFrame = true;
            sawFirstFe = false;
            continue;
          }

          // Preserve a possible new first preamble byte.
          sawFirstFe = b == 0xFE;
          continue;
        }

        frame.Add(b);

        if (b == 0xFD)
          return frame.ToArray();

        if (frame.Count >= maxFrameLength)
        {
          Log?.LogWarning(
            $"Discarding oversized/incomplete CI-V frame ({frame.Count} bytes)");
          frame.Clear();
          inFrame = false;
          sawFirstFe = false;
        }
      }

      return null;
    }

    private static bool IsCivPattern(byte?[] bytes) =>
      bytes.Length >= 2 &&
      bytes[0] == 0xFE &&
      bytes[1] == 0xFE;

    private static bool IsScopeWaveformFrame(byte[] frame) =>
      frame.Length >= 7 &&
      frame[0] == 0xFE &&
      frame[1] == 0xFE &&
      frame[4] == 0x27 &&
      frame[5] == 0x00;


    //----------------------------------------------------------------------------------------------
    //                                    read / write
    //----------------------------------------------------------------------------------------------
    private int ReceiveBytes(byte[] buffer, int offset, int count, int timeout = 1000)
    {
      int bytesRead = 0;
      SerialPort.ReadTimeout = timeout;

      while (bytesRead < count)
      {
        try
        {
            int read = SerialPort.Read(buffer, offset + bytesRead, count - bytesRead);
            if (read == 0) break;

            ScopeTap.Feed(
              new ReadOnlySpan<byte>(buffer, offset + bytesRead, read));

            bytesRead += read;
        }
        catch (TimeoutException)
        {
            break;
        }
      }
      return bytesRead;
    }

    public void DrainAsynchronousScopeTraffic()
    {
      if (!SerialPort.IsOpen) return;
      DumpUnexpectedBytes();
    }

    // Ignore bytes that arrived before the new command was sent. With IC-9700
    // scope output enabled this is normal asynchronous traffic, not an error.
    private void DumpUnexpectedBytes()
    {
      int availableBytes = SerialPort.BytesToRead;
      if (availableBytes == 0) return;

      // A drain can begin in the middle of a 27 00 frame if the preceding bytes
      // were consumed while waiting for a CAT reply. The shared ScopeTap retains
      // that partial frame across all receive paths, so a short tail such as
      // 0E-FD or 00-FD is expected asynchronous scope traffic, not corruption.
      bool continuesBufferedFrame = ScopeTap.HasPartialFrame;

      byte[] buffer = new byte[availableBytes];
      int received = ReceiveBytes(buffer, 0, availableBytes);
      if (received <= 0) return;

      bool containsScope =
        FindSequence(
          buffer.AsSpan(0, received),
          new byte[] { 0xFE, 0xFE, 0xE0, 0xA2, 0x27, 0x00 }) >= 0;

      if (containsScope || continuesBufferedFrame)
      {
        Log?.LogTrace(
          $"Drained {received} byte(s) of asynchronous CI-V/scope traffic");
        return;
      }

      // RS-BA1/virtual-COM occasionally exposes idle/padding reads consisting
      // entirely of zero bytes. They are not CI-V frames and have no command
      // semantics. Treat them as transport noise rather than alarming the user.
      if (IsZeroPadding(buffer.AsSpan(0, received)))
      {
        Log?.LogTrace(
          $"Drained {received} byte(s) of zero padding from the CAT transport");
        return;
      }

      const int previewLength = 64;
      byte[] preview = buffer.Take(Math.Min(received, previewLength)).ToArray();
      string suffix = received > previewLength
        ? $" ... ({received} bytes total)"
        : string.Empty;

      Log?.LogWarning(
        $"Unexpected bytes received before command: " +
        $"{BitConverter.ToString(preview)}{suffix}");
    }

    public static bool IsZeroPadding(ReadOnlySpan<byte> bytes)
    {
      if (bytes.Length == 0) return false;

      for (int i = 0; i < bytes.Length; i++)
        if (bytes[i] != 0)
          return false;

      return true;
    }

    private sealed class ScopeFrameTap
    {
      private const int MaximumBufferedBytes = 8192;
      private readonly Action<byte[]> Callback;
      private readonly List<byte> Buffer = new(1024);

      internal ScopeFrameTap(Action<byte[]> callback)
      {
        Callback = callback;
      }

      internal bool HasPartialFrame => Buffer.Count > 0;

      internal void FeedByte(byte value)
      {
        Buffer.Add(value);
        Parse();
      }

      internal void Feed(ReadOnlySpan<byte> bytes)
      {
        for (int i = 0; i < bytes.Length; i++)
          Buffer.Add(bytes[i]);

        Parse();
      }

      private void Parse()
      {
        while (true)
        {
          int start = FindPreamble();
          if (start < 0)
          {
            if (Buffer.Count > 0 && Buffer[^1] == 0xFE)
            {
              byte tail = Buffer[^1];
              Buffer.Clear();
              Buffer.Add(tail);
            }
            else
            {
              Buffer.Clear();
            }
            return;
          }

          if (start > 0)
            Buffer.RemoveRange(0, start);

          int end = -1;
          int nestedStart = -1;

          for (int i = 2; i < Buffer.Count; i++)
          {
            if (i + 1 < Buffer.Count &&
                Buffer[i] == 0xFE &&
                Buffer[i + 1] == 0xFE)
            {
              nestedStart = i;
              break;
            }

            if (Buffer[i] == 0xFD)
            {
              end = i;
              break;
            }
          }

          // A command/reply read can consume the tail of a scope frame that this
          // tap saw only partially. If a new FE FE arrives before FD, abandon the
          // stale partial frame and resynchronize at the newer preamble.
          if (nestedStart >= 0)
          {
            Buffer.RemoveRange(0, nestedStart);
            continue;
          }

          if (end < 0)
          {
            if (Buffer.Count > MaximumBufferedBytes)
              Buffer.Clear();
            return;
          }

          byte[] frame = Buffer.GetRange(0, end + 1).ToArray();
          Buffer.RemoveRange(0, end + 1);

          if (IsScopeWaveformFrame(frame))
            Callback(frame);
        }
      }

      private int FindPreamble()
      {
        for (int i = 0; i + 1 < Buffer.Count; i++)
          if (Buffer[i] == 0xFE && Buffer[i + 1] == 0xFE)
            return i;

        return -1;
      }
    }


    private static int FindSequence(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
      if (needle.Length == 0 || haystack.Length < needle.Length) return -1;

      for (int i = 0; i <= haystack.Length - needle.Length; i++)
        if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
          return i;

      return -1;
    }




    //----------------------------------------------------------------------------------------------
    //                                encode and decode
    //----------------------------------------------------------------------------------------------
    private byte[] ParamToBytes(ParamInfo param, string? value, int byteCount)
    {
      if (value == null) throw new ArgumentNullException(nameof(value));

      return param.Format switch
      {
        CatParamFormat.BCD_LE => EncodeBCD(param, value, byteCount).Reverse().ToArray(),
        CatParamFormat.BCD_BE => EncodeBCD(param, value, byteCount),
        CatParamFormat.Enum => EncodeEnum(param, value),
        CatParamFormat.Text => EncodeText(param, value, byteCount),
        _ => throw new ArgumentException($"Unknown param format: {param.Format}", nameof(param))
      };
    }

    private string? BytesToParam(ParamInfo param, byte[] bytes)
    {
      return param.Format switch
      {
        CatParamFormat.BCD_LE => DecodeBCD(param, bytes.Reverse().ToArray()),
        CatParamFormat.BCD_BE => DecodeBCD(param, bytes),
        CatParamFormat.Enum => DecodeEnum(param, bytes),
        CatParamFormat.Text => DecodeText(param, bytes),
        _ => throw new ArgumentException($"Unknown param format: {param.Format}", nameof(param))
      };
    }
    
    private byte[] EncodeEnum(ParamInfo param, string key)
    {
      // enums cannot have '-' in the name but some modes require it
      key = key.Replace('_', '-');

      if (!param.Values!.ContainsKey(key)) throw new ArgumentException($"Unknown enum param: {key}.", nameof(key));

      return param.Values[key];
    }

    private byte[] EncodeBCD(ParamInfo param, string value, int byteCount)
    {
      string formattedValue = FormatNumber(value, param, byteCount * 2);

      byte[] bcd = new byte[byteCount];
      for (int i = 0; i < byteCount; i++)
      {
        int highNibble = formattedValue[i * 2] - '0';
        int lowNibble = formattedValue[i * 2 + 1] - '0';
        bcd[i] = (byte)((highNibble << 4) | lowNibble);
      }

      return bcd;
    }

    private byte[] EncodeText(ParamInfo param, string value, int byteCount)
    {
      string formattedValue = FormatNumber(value, param, byteCount);
      return Encoding.ASCII.GetBytes(formattedValue);
    }

    private string DecodeEnum(ParamInfo param, byte[] bytes)
    {
      string key = param.Values!.FirstOrDefault(kv => kv.Value.SequenceEqual(bytes)).Key;
      if (key != null) return key;
      throw new ArgumentException($"Invalid enum value: {BitConverter.ToString(bytes)}", nameof(bytes));
    }

    private string DecodeBCD(ParamInfo param, byte[] bcd)
    {
      long result = 0;

      for (int i = 0; i < bcd.Length; i++)
        result = result * 100 + (bcd[i] >> 4) * 10 + (bcd[i] & 0x0F);

      if (param.Step.HasValue) result = (long)(result * param.Step.Value);

      return result.ToString();
    }

    private string DecodeText(ParamInfo param, byte[] bytes)
    {
      string stringValue = Encoding.ASCII.GetString(bytes);
      long value = long.Parse(stringValue);
      if (param.Step.HasValue) value = (long)(value * param.Step.Value);
      return value.ToString();
    }

    private static string NullableBytesToString(byte?[] bytes)
    {
      return string.Join("-", bytes.Select(b => b.HasValue ? b.Value.ToString("X2") : "xx"));
    }

    private bool BytesMatch(byte[] bytes, byte?[] mask)
    {
      if (bytes.Length != mask.Length) return false;
      for (int i = 0; i < bytes.Length; i++)
        if (mask[i] != null && bytes[i] != mask[i]) return false;
      return true;
    }

    private void ApplyMask(byte[] paramBytes, byte[] mask)
    {
      for (int i = 0; i < paramBytes.Length && i < mask.Length; i++)
        paramBytes[i] &= mask[i];
    }

    private string FormatNumber(string value, ParamInfo param, int digitCount)
    {
      if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Number cannot be null or empty.", nameof(value));
      if (!long.TryParse(value, out long numericValue)) throw new ArgumentException($"Invalid number format '{value}'.", nameof(value));

      if (param.Step.HasValue) numericValue = (long)(numericValue / param.Step.Value);

      value = numericValue.ToString($"D{digitCount}");
      if (value.Length > digitCount) throw new ArgumentException($"Number {value} has more than {digitCount} digits.");

      return value;
    }
  }
}
