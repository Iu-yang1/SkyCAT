using skycatd;

namespace SkyCat.Tests;

public sealed class IcomScopeSweepMeterTests
{
  private static byte[] Scope(byte rx, int sequence, int max) =>
    new byte[] {
      0xFE, 0xFE, 0xE0, 0xA2, 0x27, 0x00, rx,
      (byte)(((sequence / 10) << 4) | sequence % 10),
      (byte)(((max / 10) << 4) | max % 10),
      0xFD
    };

  [Fact]
  public void CountsCompletedSweepsNotChunks()
  {
    var meter = new IcomScopeSweepMeter();
    for (int i = 1; i <= 11; i++) meter.Record(Scope(0, i, 11));
    var result = meter.Snapshot();
    Assert.Equal(1, result.Completed);
    Assert.Equal(0, result.Incomplete);
    Assert.Equal(0, result.Invalid);
  }

  [Fact]
  public void TracksMainAndSubIndependently()
  {
    var meter = new IcomScopeSweepMeter();
    meter.Record(Scope(0, 1, 2));
    meter.Record(Scope(1, 1, 2));
    meter.Record(Scope(0, 2, 2));
    meter.Record(Scope(1, 2, 2));
    Assert.Equal(2, meter.Snapshot().Completed);
    Assert.Equal(0, meter.Snapshot().Incomplete);
  }

  [Fact]
  public void MissingChunkPreventsFalseCompletedSweep()
  {
    var meter = new IcomScopeSweepMeter();
    meter.Record(Scope(0, 1, 3));
    meter.Record(Scope(0, 3, 3));
    Assert.Equal(0, meter.Snapshot().Completed);
    Assert.Equal(1, meter.Snapshot().Incomplete);
    meter.Record(Scope(0, 1, 1));
    Assert.Equal(1, meter.Snapshot().Completed);
  }

  [Fact]
  public void InterruptedSweepFollowedByNewSweepIsCountedAsLoss()
  {
    var meter = new IcomScopeSweepMeter();
    meter.Record(Scope(0, 1, 11));
    meter.Record(Scope(0, 2, 11));
    meter.Record(Scope(0, 1, 1));
    Assert.Equal(1, meter.Snapshot().Completed);
    Assert.Equal(1, meter.Snapshot().Incomplete);
  }

  [Fact]
  public void InvalidBcdDoesNotIncreaseSweepCount()
  {
    var meter = new IcomScopeSweepMeter();
    byte[] invalid = Scope(0, 1, 11);
    invalid[7] = 0xFA;
    meter.Record(invalid);
    Assert.Equal(0, meter.Snapshot().Completed);
    Assert.Equal(1, meter.Snapshot().Invalid);
  }
}
