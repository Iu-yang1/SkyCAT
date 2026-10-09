using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using skycatd;

namespace SkyCat.Tests;

public sealed class ScopeStreamServerTests
{
  [Fact]
  public async Task NativeScopeTransportPreservesElevenContiguousChunksForTwoClients()
  {
    // This exercises the real 32-bit little-endian length framing and
    // per-client async sender, without needing an IC-9700 on CI.
    var server = new ScopeStreamServer(0, NullLogger.Instance);
    server.Start();
    try
    {
      Assert.InRange(server.BoundPort, 1, 65535);
      using var first = new TcpClient();
      using var second = new TcpClient();
      await first.ConnectAsync(IPAddress.Loopback, server.BoundPort);
      await second.ConnectAsync(IPAddress.Loopback, server.BoundPort);
      Assert.True(SpinWait.SpinUntil(
        () => server.GetDiagnostics().Clients == 2,
        TimeSpan.FromSeconds(3)));

      byte[][] chunks = Enumerable.Range(1, 11)
        .Select(sequence => new byte[] {
          0xFE, 0xFE, 0xE0, 0xA2, 0x27, 0x00,
          0x00, (byte)sequence, 0x11, 0xFD
        }).ToArray();

      foreach (byte[] chunk in chunks)
        server.Publish(chunk);

      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      foreach (var stream in new[] { first.GetStream(), second.GetStream() })
      {
        foreach (byte[] expected in chunks)
        {
          byte[] header = new byte[4];
          await stream.ReadExactlyAsync(header, timeout.Token);
          Assert.Equal(expected.Length, BitConverter.ToInt32(header));
          byte[] actual = new byte[expected.Length];
          await stream.ReadExactlyAsync(actual, timeout.Token);
          Assert.Equal(expected, actual);
        }
      }

      Assert.True(SpinWait.SpinUntil(
        () => server.GetDiagnostics().Sent == 22,
        TimeSpan.FromSeconds(2)));
      var snapshot = server.GetDiagnostics();
      Assert.Equal(11, snapshot.Input);
      Assert.Equal(22, snapshot.Sent);
      Assert.Equal(0, snapshot.Dropped);
    }
    finally
    {
      server.Stop();
    }
  }

  [Fact]
  public void NoClientDoesNotTurnOffScopeInputDiagnostics()
  {
    var server = new ScopeStreamServer(0, NullLogger.Instance);
    server.Publish(new byte[] { 0xFE, 0xFE, 0xFD });
    var snapshot = server.GetDiagnostics();
    Assert.Equal(1, snapshot.Input);
    Assert.Equal(0, snapshot.Sent);
    Assert.Equal(0, snapshot.Clients);
  }
}
