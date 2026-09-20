using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace skycatd
{
  public class TcpServer
  {
    private readonly int Port;
    private readonly Func<string, string> CommandHandler;
    private readonly ILogger Logger;
    private readonly IPAddress ListenAddress;
    private readonly object CommandLock;
    private readonly Action? ClientDisconnected;
    private readonly string ServerName;
    private TcpListener? Listener;
    private readonly ConcurrentBag<TcpClient> ActiveClients = new();

    public TcpServer(
      int port,
      Func<string, string> commandHandler,
      ILogger logger,
      IPAddress? listenAddress = null,
      object? commandLock = null,
      Action? clientDisconnected = null,
      string serverName = "TCP")
    {
      Port = port;
      CommandHandler = commandHandler;
      Logger = logger;
      ListenAddress = listenAddress ?? IPAddress.Any;
      CommandLock = commandLock ?? new object();
      ClientDisconnected = clientDisconnected;
      ServerName = serverName;
    }

    public void Start()
    {
      if (IsListening()) return;

      try
      {
        Listener = new TcpListener(ListenAddress, Port);
        Listener.Start();

        // start accepting clients in the background
        Task.Run(async () =>
        {
          while (Listener != null)
            try
            {
              var client = await Listener.AcceptTcpClientAsync();
              _ = Task.Run(() => HandleClient(client));
            }
            catch (Exception ex)
            {
              // Stop() called
              if (Listener == null) break;
              // failure
              else Logger.LogError($"{ServerName} server stopped: {ex.Message}");
            }
        });
      }
      catch (Exception)
      {
        Listener = null;
        throw;
      }
    }

    int NextId = 1;

    private void HandleClient(TcpClient client)
    {
      int id = Interlocked.Increment(ref NextId) - 1;
      ActiveClients.Add(client);
      var endPoint = client.Client.RemoteEndPoint;
      Logger.LogInformation($"{ServerName} client #{id} connected: {endPoint} ({ActiveClients.Count} connected clients)");

      try
      {
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
          Logger.LogDebug($"Received from {ServerName} client #{id}: '{line}'");

          string response;
          lock (CommandLock) response = CommandHandler(line);

          Logger.LogDebug($"  Replying to {ServerName} client #{id}: {AddDescription(response)}");
          writer.Write(response + "\n");
        }
      }
      catch (IOException ex)
      {
        Logger.LogDebug($"{ServerName} client #{id} disconnected with I/O error: {ex.Message}");
      }
      catch (SocketException ex)
      {
        Logger.LogDebug($"{ServerName} client #{id} disconnected with socket error: {ex.Message}");
      }
      finally
      {
        client.Close();
        ActiveClients.TryTake(out _);

        if (ClientDisconnected != null)
          try
          {
            lock (CommandLock) ClientDisconnected();
          }
          catch (Exception ex)
          {
            Logger.LogWarning($"{ServerName} disconnect cleanup failed: {ex.Message}");
          }

        Logger.LogInformation($"{ServerName} client #{id} disconnected: {endPoint} ({ActiveClients.Count} connected clients)");
      }
    }

    private string AddDescription(string response)
    {
      return response switch
      {
        "RPRT 0" => $"'{response}' (OK)",
        "RPRT -1" => $"'{response}' (invalid parameter)",
        "RPRT -5" => $"'{response}' (I/O timeout)",
        "RPRT -6" => $"'{response}' (I/O error)",
        "RPRT -7" => $"'{response}' (internal error)",
        "RPRT -9" => $"'{response}' (command rejected by the radio)",
        "RPRT -11" => $"'{response}' (function not available)",
        _ when response.StartsWith("RPRT ") => $"'{response}' (Unknown RPRT code)",
        _ => $"'{response}'"
      };
    }

    public void Stop()
    {
      if (!IsListening()) return;

      foreach (var client in ActiveClients)
        try
        {
          client.Close();
        }
        catch { }
      ActiveClients.Clear();

      Listener?.Stop();
      Listener = null;
      Logger.LogInformation($"{ServerName} server stopped.");
    }

    public bool IsListening()
    {
      return Listener != null && Listener.Server.IsBound;
    }
  }
}
