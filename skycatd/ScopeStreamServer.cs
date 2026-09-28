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

      internal ScopeClient(TcpClient client)
      {
        Client = client;
        Client.NoDelay = true;
        Stream = client.GetStream();

        // Scope is a live display, not a lossless recording stream. Keep only a
        // few recent frames so a paused/non-reading client can never backpressure
        // the serial CI-V parser or the shared CAT command lock.
        Frames = Channel.CreateBounded<byte[]>(
          new BoundedChannelOptions(3)
          {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
          });
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
      if (frame == null || frame.Length == 0 || Clients.IsEmpty)
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

    internal bool HasClients => !Clients.IsEmpty;

    internal bool IsListening() =>
      Listener != null && Listener.Server.IsBound;
  }
}
