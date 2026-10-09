using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace skycatd
{
  internal sealed class ScopeStreamServer
  {
    private sealed class ScopeClient
    {
      private readonly TcpClient Client;
      private readonly NetworkStream Stream;
      private readonly Channel<byte[]> Frames;
      private readonly CancellationTokenSource Cancellation = new();
      private Task? SenderTask;
      private int Disposed;
      private long SentChunks;
      private long DroppedChunks;
      internal long Sent => Interlocked.Read(ref SentChunks);
      internal long Dropped => Interlocked.Read(ref DroppedChunks);

      internal ScopeClient(TcpClient client)
      {
        Client = client;
        Client.NoDelay = true;
        Stream = client.GetStream();

        // Scope is a live display, not a lossless recording stream. Keep a
        // bounded recent queue so a slow TCP client cannot backpressure
        // the CI-V parser or the shared CAT command lock.
        // IC-9700 emits a sweep as up to 11 distinct 27 00 CI-V
        // chunks. A three-*chunk* buffer dropped parts of the same sweep
        // during brief network scheduling stalls, forcing the SkyRoof
        // assembler to discard incomplete sweeps. Keep several complete
        // sweeps of headroom while remaining strictly bounded.
        Frames = Channel.CreateBounded<byte[]>(
          new BoundedChannelOptions(64)
          {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
          },
          _ => Interlocked.Increment(ref DroppedChunks));
      }

      internal void Start(Action<Exception?> disconnected)
      {
        SenderTask = Task.Run(() => SendLoop(disconnected));
      }

      internal bool TryPublish(byte[] frame) =>
        Frames.Writer.TryWrite(frame);

      private async Task SendLoop(Action<Exception?> disconnected)
      {
        Exception? failure = null;

        try
        {
          await foreach (byte[] frame in
            Frames.Reader.ReadAllAsync(Cancellation.Token))
          {
            byte[] header =
            {
              (byte)(frame.Length & 0xFF),
              (byte)((frame.Length >> 8) & 0xFF),
              (byte)((frame.Length >> 16) & 0xFF),
              (byte)((frame.Length >> 24) & 0xFF)
            };

            await Stream.WriteAsync(header, Cancellation.Token);
            await Stream.WriteAsync(frame, Cancellation.Token);
            Interlocked.Increment(ref SentChunks);
          }
        }
        catch (OperationCanceledException)
        {
          // Normal Stop()/disconnect path.
        }
        catch (Exception ex)
        {
          failure = ex;
        }
        finally
        {
          disconnected(failure);
        }
      }

      internal void Dispose()
      {
        if (Interlocked.Exchange(ref Disposed, 1) != 0)
          return;

        Frames.Writer.TryComplete();
        try { Cancellation.Cancel(); } catch { }
        try { Stream.Dispose(); } catch { }
        try { Client.Close(); } catch { }
      }
    }

    private readonly int Port;
    private readonly ILogger Logger;
    private TcpListener? Listener;
    private readonly ConcurrentDictionary<int, ScopeClient> Clients = new();
    private int NextClientId;
    private long InputChunks;
    private long HistoricalSent;
    private long HistoricalDropped;

    internal (long Input, long Sent, long Dropped, int Clients) GetDiagnostics()
    {
      long sent = Interlocked.Read(ref HistoricalSent);
      long dropped = Interlocked.Read(ref HistoricalDropped);
      foreach (ScopeClient client in Clients.Values)
      {
        sent += client.Sent;
        dropped += client.Dropped;
      }
      return (Interlocked.Read(ref InputChunks), sent, dropped, Clients.Count);
    }

    internal ScopeStreamServer(int port, ILogger logger)
    {
      Port = port;
      Logger = logger;
    }

    internal void Start()
    {
      if (IsListening()) return;

      var listener = new TcpListener(IPAddress.Loopback, Port);
      listener.Start();
      Listener = listener;

      _ = Task.Run(async () =>
      {
        while (ReferenceEquals(Listener, listener))
        {
          try
          {
            TcpClient tcpClient = await listener.AcceptTcpClientAsync();

            if (!ReferenceEquals(Listener, listener))
            {
              tcpClient.Close();
              break;
            }

            int id = Interlocked.Increment(ref NextClientId);
            var client = new ScopeClient(tcpClient);
            Clients[id] = client;
            client.Start(error => RemoveClient(id, error));

            Logger.LogInformation(
              $"Scope stream client #{id} connected: {tcpClient.Client.RemoteEndPoint} " +
              $"({Clients.Count} connected clients)");
          }
          catch (ObjectDisposedException)
          {
            break;
          }
          catch (SocketException ex)
          {
            if (!ReferenceEquals(Listener, listener)) break;
            Logger.LogWarning($"Scope stream accept failed: {ex.Message}");
          }
          catch (Exception ex)
          {
            if (!ReferenceEquals(Listener, listener)) break;
            Logger.LogWarning($"Scope stream accept failed: {ex.Message}");
          }
        }
      });
    }

    internal void Publish(byte[] frame)
    {
      if (frame == null || frame.Length == 0)
        return;
      Interlocked.Increment(ref InputChunks);
      if (Clients.IsEmpty)
        return;

      // Never perform socket I/O on the serial/CAT thread. Each client has a
      // bounded queue and its own sender; when a client cannot keep up, older
      // spectrum frames are discarded in favor of current display data.
      foreach (var entry in Clients.ToArray())
        if (!entry.Value.TryPublish(frame))
          RemoveClient(entry.Key, null);
    }

    private void RemoveClient(int id, Exception? error)
    {
      if (!Clients.TryRemove(id, out ScopeClient? client))
        return;

      Interlocked.Add(ref HistoricalSent, client.Sent);
      Interlocked.Add(ref HistoricalDropped, client.Dropped);
      client.Dispose();

      if (error != null)
        Logger.LogDebug(
          $"Scope stream client #{id} sender stopped: {error.Message}");

      Logger.LogInformation(
        $"Scope stream client #{id} disconnected " +
        $"({Clients.Count} connected clients)");
    }

    internal void Stop()
    {
      TcpListener? listener = Listener;
      Listener = null;

      try { listener?.Stop(); } catch { }

      foreach (var entry in Clients.ToArray())
        RemoveClient(entry.Key, null);

      Logger.LogInformation("Scope stream server stopped.");
    }

    // Exposed for deterministic loopback integration tests using port 0.
    internal int BoundPort => (Listener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;

    internal bool HasClients => !Clients.IsEmpty;

    internal bool IsListening() =>
      Listener != null && Listener.Server.IsBound;
  }
}
