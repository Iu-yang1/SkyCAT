using Microsoft.Extensions.Logging;
using System.Globalization;
using Serilog.Core;
using SkyCat;
namespace skycatd
{
  public class CommandInterpreter
  {
    public readonly CatCommandSender CommandSender;

    public CommandInterpreter(Options options, ILogger? logger)
    {
      CommandSender = new CatCommandSender(logger);
      CommandSender.SelectRadio(options.Model);
      CommandSender.SerialPort.PortName = options.RigFile;
      CommandSender.SerialPort.BaudRate = options.SerialSpeed ?? CommandSender.CommandSet.DefaultBaudRate;
    }

    public string Execute(string command)
    {
      var args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      if (args.Length == 0) return "RPRT -1";

      return args[0] switch
      {
        "a" => CommandSender.ListCapabilities(),
        "f" => CmdF(),
        "i" => CmdI(),
        "m" => CmdM(),
        "x" => CmdX(),
        "t" => CmdT(),
        "F" when args.Length == 2 && long.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var fHz) => CmdFInt(fHz),
        "I" when args.Length == 2 && long.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iHz) => CmdIInt(iHz),
        "M" when args.Length == 3 && int.TryParse(args[2], out var mZero) => CmdMStr0(args[1], mZero),
        "X" when args.Length == 3 && int.TryParse(args[2], out var xZero) => CmdXStr0(args[1], xZero),
        "T" when args.Length == 2 && (args[1] == "0") => CmdT0("OFF"),
        "T" when args.Length == 2 && (args[1] == "1") => CmdT1("ON"),

        // CTCSS transmit encode tone (tone value in tenths of Hz, e.g. C 670 = 67.0 Hz)
        "C" when args.Length == 2 && int.TryParse(args[1], out var toneTenthsHz) => CmdC(toneTenthsHz),
        "U" when args.Length == 3 && args[1] == "TONE" && args[2] == "1" => SendCommandIfAvailable(CatCommand.enable_ctcss),
        "U" when args.Length == 3 && args[1] == "TONE" && args[2] == "0" => SendCommandIfAvailable(CatCommand.disable_ctcss),
        "U" when args.Length == 3 && args[1] == "SCOPE" && args[2] == "1" => SendCommandIfAvailable(CatCommand.enable_scope),
        "U" when args.Length == 3 && args[1] == "SCOPE" && args[2] == "0" => SendCommandIfAvailable(CatCommand.disable_scope),
        "U" when args.Length == 3 && args[1] == "SCOPE_DATA" && args[2] == "1" => SendCommandIfAvailable(CatCommand.enable_scope_data),
        "U" when args.Length == 3 && args[1] == "SCOPE_DATA" && args[2] == "0" => SendCommandIfAvailable(CatCommand.disable_scope_data),
        "U" when args.Length == 3 && args[1] == "SCOPE_FAST" && args[2] == "1" => SetScopeFast(),
        "U" when args.Length == 2 && args[1] == "RF_GAIN_READ" =>
          ReadIcomRfGain(),
        "U" when args.Length == 3 && args[1] == "RF_GAIN" =>
          WriteIcomRfGain(args[2]),
        "U" when args.Length == 2 && args[1] == "CW_CAPS" =>
          GetCwCapabilities(),
        "U" when args.Length == 3 && args[1] == "CW_SEND64" =>
          SendCwMessage(args[2]),
        "U" when args.Length == 2 && args[1] == "CW_ABORT" =>
          AbortCwMessage(),
        // SkyRoof's full spectrum-control protocol. The former interpreter
        // rejected all of these with RPRT -11, leaving controls waiting for
        // SCOPE_READ forever even though the waveform stream was live.
        "U" when args.Length >= 2 &&
                 args[1].StartsWith("SCOPE_", StringComparison.Ordinal) =>
          IcomScopeCommands.Execute(CommandSender, args),

        // setup
        "S" when args.Length == 3 && args[1] == "0" => Setup(OperatingMode.Simplex),
        "S" when args.Length == 3 && args[1] == "1" && args[2] != "Sub" => Setup(OperatingMode.Split),
        "S" when args.Length == 3 && args[1] == "1" && args[2] == "Sub" => Setup(OperatingMode.Duplex),
        "U" when args.Length == 3 && args[1] == "SATMODE" && args[2] == "1" => Setup(OperatingMode.Duplex),
        "U" when args.Length == 2 && Enum.TryParse<OperatingMode>(args[1], out var mode) => Setup(mode),

        // ignore
        "U" when args.Length == 3 && args[1] == "SATMODE" && args[2] != "1" => Ignore(),
        "U" when args.Length == 3 && args[1] == "DUAL_WATCH" => Ignore(),
        "V" => Ignore(),

        _ => "RPRT -11"
      };
    }

    private string Ignore()
    {
      CommandSender.Log?.LogTrace("  Command ignored");
      return "RPRT 0";
    }

    private string CmdF() => SendCommandIfAvailable(CatCommand.read_rx_frequency);
    private string CmdI() => SendCommandIfAvailable(CatCommand.read_tx_frequency);
    private string CmdM() => SendCommandIfAvailable(CatCommand.read_rx_mode);
    private string CmdX() => SendCommandIfAvailable(CatCommand.read_tx_mode);
    private string CmdT() => SendCommandIfAvailable(CatCommand.read_ptt);
    private string CmdFInt(long value) => SendCommandIfAvailable(CatCommand.write_rx_frequency, value.ToString());
    private string CmdIInt(long value) => SendCommandIfAvailable(CatCommand.write_tx_frequency, value.ToString());
    private string CmdMStr0(string str, int zero) => SendCommandIfAvailable(CatCommand.write_rx_mode, str);
    private string CmdXStr0(string str, int zero) => SendCommandIfAvailable(CatCommand.write_tx_mode, str);
    private string CmdT0(string value) => SendCommandIfAvailable(CatCommand.write_ptt_off, value);
    private string CmdT1(string value) => SendCommandIfAvailable(CatCommand.write_ptt_on, value);
    private string CmdC(int toneTenthsHz) => SendCommandIfAvailable(CatCommand.write_ctcss_tone, toneTenthsHz.ToString());

    private string GetCwCapabilities()
    {
      if (!string.Equals(
            CommandSender.RadioName,
            "IC-9700",
            StringComparison.OrdinalIgnoreCase))
        return "RPRT -11";

      return
        "{\"version\":1," +
        "\"model\":\"IC-9700\"," +
        "\"max_chars\":" +
        IcomCwMessageCodec.MaxCharacters +
        "," +
        "\"encoding\":\"base64-ascii\"," +
        "\"abort\":true}";
    }

    private string SendCwMessage(
      string encodedPayload)
    {
      if (!string.Equals(
            CommandSender.RadioName,
            "IC-9700",
            StringComparison.OrdinalIgnoreCase))
        return "RPRT -11";

      try
      {
        string text =
          IcomCwMessageCodec
            .DecodeBase64Payload(
              encodedPayload);

        CommandSender.SendIcomCwMessage(
          text);
        return "RPRT 0";
      }
      catch (Exception ex)
      {
        return FormatCwError(ex);
      }
    }

    private string AbortCwMessage()
    {
      if (!string.Equals(
            CommandSender.RadioName,
            "IC-9700",
            StringComparison.OrdinalIgnoreCase))
        return "RPRT -11";

      try
      {
        CommandSender
          .AbortIcomCwMessage();
        return "RPRT 0";
      }
      catch (Exception ex)
      {
        return FormatCwError(ex);
      }
    }

    private string FormatCwError(
      Exception ex)
    {
      CommandSender.Log?.LogWarning(
        "IC-9700 CW message CAT operation failed: {Error}",
        ex.Message);

      return ex switch
      {
        NotSupportedException =>
          "RPRT -11",
        ArgumentException =>
          "RPRT -1",
        TimeoutException =>
          "RPRT -5",
        InvalidOperationException =>
          "RPRT -6",
        InvalidReplyException or
          FormatException =>
          "RPRT -9",
        _ =>
          "RPRT -7"
      };
    }


    private string ReadIcomRfGain()
    {
      try
      {
        return CommandSender.ReadIcomRfGain().ToString(
          System.Globalization.CultureInfo.InvariantCulture);
      }
      catch (Exception ex)
      {
        return FormatRfGainError(ex);
      }
    }

    private string WriteIcomRfGain(string rawValue)
    {
      if (!int.TryParse(rawValue, System.Globalization.NumberStyles.None,
          System.Globalization.CultureInfo.InvariantCulture, out int gain) ||
          gain is < 0 or > 255)
        return "RPRT -1";

      try
      {
        CommandSender.SetIcomRfGain(gain);
        return "RPRT 0";
      }
      catch (Exception ex)
      {
        return FormatRfGainError(ex);
      }
    }

    private string FormatRfGainError(Exception ex)
    {
      CommandSender.Log?.LogWarning(
        "IC-9700 RF gain CAT operation failed: {Error}", ex.Message);
      return ex switch {
        NotSupportedException => "RPRT -11",
        InvalidOperationException => "RPRT -6",
        ArgumentException => "RPRT -1",
        TimeoutException => "RPRT -5",
        InvalidReplyException or FormatException => "RPRT -9",
        _ => "RPRT -7"
      };
    }

    private string SetScopeFast()
    {
      try
      {
        CommandSender.SetIcomScopeSweepFast();
        return "RPRT 0";
      }
      catch (InvalidReplyException ex)
      {
        CommandSender.Log?.LogError($"Scope speed command rejected: {ex.Message}");
        return "RPRT -9";
      }
      catch (TimeoutException ex)
      {
        CommandSender.Log?.LogError($"Scope speed command timed out: {ex.Message}");
        return "RPRT -5";
      }
      catch (InvalidOperationException ex)
      {
        CommandSender.Log?.LogError($"Scope speed command failed: {ex.Message}");
        return "RPRT -6";
      }
      catch (Exception ex)
      {
        CommandSender.Log?.LogError(ex, "Scope speed command failed.");
        return "RPRT -7";
      }
    }

    private string Setup(OperatingMode mode)
    {
      try
      {
        CommandSender.SetupRadio(mode);
        return "RPRT 0";
      }
      catch (Exception ex)
      {
        CommandSender.Log?.LogError(ex, $"Setup command failed: {ex.Message}");
        return "RPRT -1";
      }
    }


    private string SendCommandIfAvailable(CatCommand command, string? paramValue = null)
    {
      if (command != CatCommand.setup && !CommandSender.IsCommandAvailable(command)) return "RPRT -11";

      try
      {
        return CommandSender.SendCommand(command, paramValue) ?? "RPRT 0";
      }

      catch (ArgumentException ex)
      {
        CommandSender.Log?.LogError($"Command failed: {ex.Message}");
        return "RPRT -1";
      }
      catch (TimeoutException ex)
      {
        CommandSender.Log?.LogError($"Command failed: {ex.Message}");
        return "RPRT -5";
      }
      catch (InvalidOperationException ex)
      {
        CommandSender.Log?.LogError($"Command failed: {ex.Message}");
        return "RPRT -6";
      }
      catch (InvalidReplyException ex)
      {
        CommandSender.Log?.LogError($"Command failed: {ex.Message}");
        return "RPRT -9";
      }
      catch (Exception ex)
      {
        CommandSender.Log?.LogError(ex, "Command failed.");
        return "RPRT -7";
      }
    }
  }
}