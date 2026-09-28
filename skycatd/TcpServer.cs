using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace skycatd
{
  public sealed record TcpClientSession(Func<string, string> Execute, Action? Disconnect = null);

  public class TcpServer
  {
    private readonly int Port;
    private readonly Func<string, string> CommandHandler;
    private readonly ILogger Logger;
    private readonly IPAddress ListenAddress;
    private readonly object CommandLock;
    private readonly Action? ClientDisconnected;
    private readonly Func<TcpClientSession>? ClientSessionFactory;
    private readonly string ServerName;
    private TcpListener? Listener;
    private volatile bool Stopping;
    private readonly ConcurrentDictionary<int, TcpClient> ActiveClients = new();
    private readonly ConcurrentDictionary<int, Task> ActiveHandlers = new();

    public TcpServer(
      int port,
      Func<string, string> commandHandler,
      ILogger logger,
      IPAddress? listenAddress = null,
      object? commandLock = null,
      Action? clientDisconnected = null,
      string serverName = "TCP",
      Func<TcpClientSession>? clientSessionFactory = null)
    {
      Port = port;
      CommandHandler = commandHandler;
      Logger = logger;
      ListenAddress = listenAddress ?? IPAddress.Any;
      CommandLock = commandLock ?? new object();
      ClientDisconnected = clientDisconnected;
      ClientSessionFactory = clientSessionFactory;
      ServerName = serverName;
    }

    public void Start()
    {
      if (IsListening()) return;

      Stopping = false;
      TcpListener? listener = null;

      try
      {
        listener = new TcpListener(ListenAddress, Port);
        listener.Start();
        Listener = listener;

        // Bind this accept loop to the exact listener instance that created it.
        // Stop()/Start() may replace Listener; an old task must never begin
        // accepting from the new listener as a second competing loop.
        _ = Task.Run(async () =>
        {
          while (ReferenceEquals(Listener, listener))
          {
            try
            {
              TcpClient client = await listener.AcceptTcpClientAsync();

              if (!ReferenceEquals(Listener, listener))
              {
                client.Close();
                break;
              }

              int id = Interlocked.Increment(ref NextId) - 1;
              var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
              ActiveHandlers[id] = completion.Task;

              _ = Task.Run(() =>
              {
                try { HandleClient(client, id); }
                finally
                {
                  completion.TrySetResult();
                  ActiveHandlers.TryRemove(id, out _);
                }
              });
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

    private void HandleClient(TcpClient client, int id)
    {
      TcpClientSession? session = ClientSessionFactory?.Invoke();
      Func<string, string> commandHandler = session?.Execute ?? CommandHandler;
      Action? clientDisconnected = session?.Disconnect ?? ClientDisconnected;

      ActiveClients[id] = client;
      var endPoint = client.Client.RemoteEndPoint;
      Logger.LogInformation($"{ServerName} client #{id} connected: {endPoint} ({ActiveClients.Count} connected clients)");

      try
      {
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

        string? line;
        while (!Stopping && (line = reader.ReadLine()) != null)
        {
          Logger.LogDebug($"Received from {ServerName} client #{id}: '{line}'");

          string response;
          lock (CommandLock)
          {
            // Stop() closes sockets and then performs the final radio fail-safe.
            // Re-check while holding the same lock so a line read just before
            // shutdown can never key the transmitter after that final PTT OFF.
            if (Stopping) break;
            response = commandHandler(line);
          }

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
        ActiveClients.TryRemove(id, out _);

        if (clientDisconnected != null)
          try
          {
            lock (CommandLock) clientDisconnected();
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
      // Gate command execution before closing sockets. HandleClient re-checks
      // this flag while holding CommandLock, so no already-read write command
      // can run after the server's final shutdown PTT release.
      Stopping = true;

      TcpListener? listener = Listener;
      Listener = null;

      try { listener?.Stop(); } catch { }

      // Close tracked clients even if the listener has already failed. Their
      // per-client disconnect callbacks run while the serial port is still open.
      foreach (TcpClient client in ActiveClients.Values.ToArray())
        try { client.Close(); } catch { }

      Task[] handlers = ActiveHandlers.Values.ToArray();
      if (handlers.Length > 0)
        try
        {
          if (!Task.WaitAll(handlers, TimeSpan.FromSeconds(5)))
            Logger.LogWarning($"{ServerName} client handlers did not fully drain before shutdown.");
        }
        catch (AggregateException ex)
        {
          Logger.LogDebug($"{ServerName} client drain completed with errors: {ex.Flatten().Message}");
        }

      Logger.LogInformation($"{ServerName} server stopped.");
    }

    public bool IsListening()
    {
      return Listener != null && Listener.Server.IsBound;
    }
  }
}
