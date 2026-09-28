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
    private readonly ConcurrentDictionary<int, TcpClient> ActiveClients = new();

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

      TcpListener? listener = null;

      try
      {
        listener = new TcpListener(ListenAddress, Port);
        listener.Start();
        Listener = listener;

        // Capture the listener instance in this accept loop. If Stop() followed by
        // Start() replaces Listener, the old task must never begin accepting on the
        // new listener as well.
        _ = Task.Run(async () =>
        {
          while (ReferenceEquals(Listener, listener))
          {
            try
            {
              var client = await listener.AcceptTcpClientAsync();

              if (!ReferenceEquals(Listener, listener))
              {
                client.Close();
                break;
              }

              _ = Task.Run(() => HandleClient(client));
            }
            catch (ObjectDisposedException)
            {
              break;
            }
            catch (SocketException ex)
            {
              if (!ReferenceEquals(Listener, listener)) break;
              Logger.LogError($"{ServerName} accept failed: {ex.Message}");
            }
            catch (Exception ex)
            {
              if (!ReferenceEquals(Listener, listener)) break;
              Logger.LogError($"{ServerName} accept failed: {ex.Message}");
            }
          }
        });
      }
      catch
      {
        if (ReferenceEquals(Listener, listener))
          Listener = null;

        try { listener?.Stop(); } catch { }
        throw;
      }
    }

    int NextId = 1;

    private void HandleClient(TcpClient client)
    {
      int id = Interlocked.Increment(ref NextId) - 1;
      ActiveClients[id] = client;
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
        if (ActiveClients.TryRemove(id, out TcpClient? trackedClient))
        {
          try { trackedClient.Close(); } catch { }
        }
        else
        {
          try { client.Close(); } catch { }
        }

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
      TcpListener? listener = Listener;
      if (listener == null) return;

      Listener = null;
      try { listener.Stop(); } catch { }

      foreach (var entry in ActiveClients.ToArray())
      {
        if (!ActiveClients.TryRemove(entry.Key, out TcpClient? client))
          continue;

        try { client.Close(); } catch { }
      }

      Logger.LogInformation($"{ServerName} server stopped.");
    }

    public bool IsListening()
    {
      return Listener != null && Listener.Server.IsBound;
    }
  }
}
