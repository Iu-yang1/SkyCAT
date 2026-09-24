using System.IO.Ports;
using System.Net;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SkyCat;

namespace skycatd
{

  public class CatServer
  {
    // log level of a message will be selected based on the port status
    private enum PortStatus { NeverOpened, WasOpen, WasClosed }

    private readonly Options options;
    private readonly CancellationTokenSource cts = new();
    private readonly Microsoft.Extensions.Logging.ILogger logger;
    private readonly TcpServer tcpServer;
    private readonly TcpServer? wsjtXTcpServer;
    private readonly ScopeStreamServer scopeStreamServer;
    private readonly CommandInterpreter commandInterpreter;
    private readonly WsjtXCommandInterpreter? wsjtXInterpreter;
    private readonly CatCommandSender commandSender;
    private readonly SerialPort serialPort;
    private readonly object commandLock = new();
    private Task? ScopeDrainTask;

    private PortStatus ComStatus;
    private PortStatus TcpStatus;
    private PortStatus WsjtXTcpStatus;
    private PortStatus ScopeTcpStatus;

    public CatServer(Options options)
    {
      this.options = options;

      logger = CreateLogger(options);

      var version = typeof(Program).Assembly.GetName().Version;
      logger.LogInformation($"Starting CAT server: skycatd v.{version}.");

      commandInterpreter = new CommandInterpreter(options, logger);
      commandSender = commandInterpreter.CommandSender;
      serialPort = commandSender.SerialPort;

      scopeStreamServer = new ScopeStreamServer(options.ScopePort, logger);
      commandSender.ScopeFrameReceived += scopeStreamServer.Publish;

      tcpServer = new TcpServer(
        options.Port,
        commandInterpreter.Execute,
        logger,
        IPAddress.Any,
        commandLock,
        serverName: "CAT");

      if (!options.DisableWsjtXProxy)
      {
        wsjtXInterpreter = new WsjtXCommandInterpreter(commandInterpreter.Execute, logger);
        wsjtXTcpServer = new TcpServer(
          options.WsjtXPort,
          wsjtXInterpreter.Execute,
          logger,
          IPAddress.Loopback,
          commandLock,
          wsjtXInterpreter.EnsurePttOff,
          "WSJT-X proxy");
      }
    }

    ConsoleTheme theme = new AnsiConsoleTheme(new Dictionary<ConsoleThemeStyle, string>
    {
      [ConsoleThemeStyle.LevelFatal] = "\x1b[41m\x1b[37m", // white on red background
      [ConsoleThemeStyle.LevelError] = "\x1b[1;31m",       // red
      [ConsoleThemeStyle.LevelWarning] = "\x1b[1;33m",     // yellow
      [ConsoleThemeStyle.LevelInformation] = "\x1b[1;32m", // green
      [ConsoleThemeStyle.LevelDebug] = "\x1b[1;37m",       // white
      [ConsoleThemeStyle.LevelVerbose] = "\x1b[37m",       // gray
      [ConsoleThemeStyle.Text] = "\x1b[37m",               // gray
    });

    private Microsoft.Extensions.Logging.ILogger CreateLogger(Options options)
    {
      var loggerConfig = new LoggerConfiguration()
        .MinimumLevel.Is(options.LogLevel)
        .WriteTo.Console(
            outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
            theme: theme
        );

      if (options.FileLog)
      {
        string logFilePath = $"Logs\\skycatd_{DateTime.Now:yyyy-MM-dd_HHmmss}.log";

        loggerConfig = loggerConfig.WriteTo.File(
            logFilePath,
            outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
            rollingInterval: RollingInterval.Infinite,
            shared: true
        );

        Console.WriteLine($"Log file created at: {Path.GetFullPath(logFilePath)}\n");
      }

      Log.Logger = loggerConfig.CreateLogger();
      var loggerFactory = new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger);
      return loggerFactory.CreateLogger(typeof(CatServer).FullName ?? nameof(CatServer));
    }

    private void SleepWithCancellation(int totalMilliseconds)
    {
      const int interval = 100;
      int elapsed = 0;

      while (elapsed < totalMilliseconds && !cts.Token.IsCancellationRequested)
      {
        Thread.Sleep(interval);
        elapsed += interval;
      }
    }

    private async Task DrainScopeTrafficLoop()
    {
      while (!cts.Token.IsCancellationRequested)
      {
        try
        {
          if (serialPort.IsOpen && scopeStreamServer.HasClients)
          {
            lock (commandLock)
            {
              if (serialPort.IsOpen)
                commandSender.DrainAsynchronousScopeTraffic();
            }

            await Task.Delay(15, cts.Token);
          }
          else
          {
            await Task.Delay(100, cts.Token);
          }
        }
        catch (OperationCanceledException)
        {
          break;
        }
        catch (Exception ex)
        {
          logger.LogDebug($"Scope traffic drain paused after error: {ex.Message}");
          try { await Task.Delay(100, cts.Token); }
          catch (OperationCanceledException) { break; }
        }
      }
    }

    public void Run()
    {
      ScopeDrainTask ??= Task.Run(DrainScopeTrafficLoop);

      while (!cts.Token.IsCancellationRequested)
      {
        if (!serialPort.IsOpen)
          // try to open com port
          try
          {
            if (ComStatus == PortStatus.WasOpen) logger.LogWarning("Serial port closed unexpectedly. Reopening.");
            else if (ComStatus == PortStatus.NeverOpened) logger.LogInformation($"Opening serial port {options.RigFile} at {serialPort.BaudRate} Baud...");
            else logger.LogTrace("Opening serial port...");

            serialPort.Open();
            ComStatus = PortStatus.WasOpen;
            Thread.Sleep(300);

            logger.LogInformation("Serial port opened.");
          }
          catch (Exception ex)
          {
            string message = $"Failed to open serial port: {ex.Message} Will retry.";
            if (ComStatus == PortStatus.WasClosed) logger.LogTrace(message); else logger.LogWarning(message);
            tcpServer.Stop();
            wsjtXTcpServer?.Stop();
            scopeStreamServer.Stop();
            ComStatus = PortStatus.WasClosed;
            TcpStatus = PortStatus.WasClosed;
            WsjtXTcpStatus = PortStatus.WasClosed;
            ScopeTcpStatus = PortStatus.WasClosed;
          }

        // if com is open, try to start the main SkyCAT TCP server
        if (serialPort.IsOpen && !tcpServer.IsListening())
          try
          {
            if (TcpStatus == PortStatus.WasOpen) logger.LogInformation("CAT TCP server stopped unexpectedly. Restarting.");
            tcpServer.Start();
            TcpStatus = PortStatus.WasOpen;
            logger.LogInformation($"CAT TCP server started on 0.0.0.0:{options.Port}.");
          }
          catch (Exception ex)
          {
            string message = $"Failed to start CAT TCP server: {ex.Message} Will retry.";
            if (TcpStatus == PortStatus.WasClosed) logger.LogTrace(message); else logger.LogWarning(message);
            TcpStatus = PortStatus.WasClosed;
          }

        // WSJT-X compatibility is intentionally loopback-only.
        if (serialPort.IsOpen && wsjtXTcpServer != null && !wsjtXTcpServer.IsListening())
          try
          {
            if (WsjtXTcpStatus == PortStatus.WasOpen) logger.LogInformation("WSJT-X proxy stopped unexpectedly. Restarting.");
            wsjtXTcpServer.Start();
            WsjtXTcpStatus = PortStatus.WasOpen;
            logger.LogInformation($"WSJT-X proxy started on 127.0.0.1:{options.WsjtXPort}.");
          }
          catch (Exception ex)
          {
            string message = $"Failed to start WSJT-X proxy: {ex.Message} Will retry.";
            if (WsjtXTcpStatus == PortStatus.WasClosed) logger.LogTrace(message); else logger.LogWarning(message);
            WsjtXTcpStatus = PortStatus.WasClosed;
          }

        // Native IC-9700 scope frames are exported separately so scope traffic can
        // never corrupt the line-oriented CAT/rigctl protocol.
        if (serialPort.IsOpen && !scopeStreamServer.IsListening())
          try
          {
            if (ScopeTcpStatus == PortStatus.WasOpen) logger.LogInformation("Scope stream server stopped unexpectedly. Restarting.");
            scopeStreamServer.Start();
            ScopeTcpStatus = PortStatus.WasOpen;
            logger.LogInformation($"Scope stream server started on 127.0.0.1:{options.ScopePort}.");
          }
          catch (Exception ex)
          {
            string message = $"Failed to start scope stream server: {ex.Message} Will retry.";
            if (ScopeTcpStatus == PortStatus.WasClosed) logger.LogTrace(message); else logger.LogWarning(message);
            ScopeTcpStatus = PortStatus.WasClosed;
          }

        SleepWithCancellation(2000);
      }

      wsjtXTcpServer?.Stop();
      scopeStreamServer.Stop();
      tcpServer.Stop();
      commandSender.ScopeFrameReceived -= scopeStreamServer.Publish;
      if (serialPort.IsOpen) serialPort.Close();

      if (ScopeDrainTask != null)
      {
        try { ScopeDrainTask.Wait(500); }
        catch (AggregateException) { }
      }

      logger.LogInformation("CatServer shutting down.");
    }

    public void Stop()
    {
      cts.Cancel();
    }
  }
}
