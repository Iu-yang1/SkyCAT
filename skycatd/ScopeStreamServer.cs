using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace skycatd
{
  internal sealed class ScopeStreamServer
  {
    private sealed class ScopeClient
    {
      internal readonly TcpClient Client;
      internal readonly NetworkStream Stream;
      internal readonly object WriteLock = new();

      internal ScopeClient(TcpClient client)
      {
        Client = client;
        Client.NoDelay = true;
        Client.SendTimeout = 100;
        Stream = client.GetStream();
      }

      internal void Dispose()
      {
        try { Stream.Dispose(); } catch { }
        try { Client.Close(); } catch { }
      }
    }

    private readonly int Port;
    private readonly ILogger Logger;
    private TcpListener? Listener;
    private readonly ConcurrentDictionary<int, ScopeClient> Clients = new();
    private int NextClientId;

    internal ScopeStreamServer(int port, ILogger logger)
    {
      Port = port;
      Logger = logger;
    }

    internal void Start()
    {
      if (IsListening()) return;

      Listener = new TcpListener(IPAddress.Loopback, Port);
      Listener.Start();

      _ = Task.Run(async () =>
      {
        while (Listener != null)
        {
          try
          {
            TcpClient tcpClient = await Listener.AcceptTcpClientAsync();
            int id = Interlocked.Increment(ref NextClientId);
            var client = new ScopeClient(tcpClient);
            Clients[id] = client;

            Logger.LogInformation(
              $"Scope stream client #{id} connected: {tcpClient.Client.RemoteEndPoint} " +
              $"({Clients.Count} connected clients)");
          }
          catch (ObjectDisposedException)
          {
            break;
          }
          catch (SocketException)
          {
            if (Listener == null) break;
          }
          catch (Exception ex)
          {
            if (Listener == null) break;
            Logger.LogWarning($"Scope stream accept failed: {ex.Message}");
          }
        }
      });
    }

    internal void Publish(byte[] frame)
    {
      if (frame == null || frame.Length == 0 || Clients.IsEmpty)
        return;

      Span<byte> header = stackalloc byte[4];
      int length = frame.Length;
      header[0] = (byte)(length & 0xFF);
      header[1] = (byte)((length >> 8) & 0xFF);
      header[2] = (byte)((length >> 16) & 0xFF);
      header[3] = (byte)((length >> 24) & 0xFF);

      foreach (var entry in Clients.ToArray())
      {
        ScopeClient client = entry.Value;

        try
        {
          lock (client.WriteLock)
          {
            client.Stream.Write(header);
            client.Stream.Write(frame, 0, frame.Length);
          }
        }
        catch (Exception)
        {
          if (Clients.TryRemove(entry.Key, out ScopeClient? removed))
          {
            removed.Dispose();
            Logger.LogInformation(
              $"Scope stream client #{entry.Key} disconnected " +
              $"({Clients.Count} connected clients)");
          }
        }
      }
    }

    internal void Stop()
    {
      TcpListener? listener = Listener;
      Listener = null;

      try { listener?.Stop(); } catch { }

      foreach (var entry in Clients.ToArray())
      {
        if (Clients.TryRemove(entry.Key, out ScopeClient? client))
          client.Dispose();
      }

      Logger.LogInformation("Scope stream server stopped.");
    }

    internal bool HasClients => !Clients.IsEmpty;

    internal bool IsListening() =>
      Listener != null && Listener.Server.IsBound;
  }
}
