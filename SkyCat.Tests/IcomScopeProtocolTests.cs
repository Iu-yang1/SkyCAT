using SkyCat;

namespace SkyCat.Tests
{
  public class IcomScopeProtocolTests
  {
    [Fact]
    public void QueryModeUsesReceiverSelector()
    {
      Assert.Equal(
        new byte[]
        {
          0xFE, 0xFE, 0xA2, 0xE0,
          0x27, 0x14, 0x00, 0xFD
        },
        IcomScopeProtocol.BuildQuery(
          IcomScopeReceiver.Main,
          IcomScopeProtocol.ModeSubcommand));
    }

    [Fact]
    public void SpanUsesFiveByteLittleEndianBcdFrequency()
    {
      Assert.Equal(
        new byte[]
        {
          0x00, 0x25, 0x00, 0x00, 0x00
        },
        IcomScopeProtocol.EncodeSpan(2_500));

      Assert.Equal(
        new byte[]
        {
          0x00, 0x00, 0x10, 0x00, 0x00
        },
        IcomScopeProtocol.EncodeSpan(100_000));

      Assert.Equal(
        100_000,
        IcomScopeProtocol.DecodeSpan(
          new byte[]
          {
            0x00, 0x00, 0x10, 0x00, 0x00
          }));
    }

    [Theory]
    [InlineData(0, 0x00, 0x00, 0x00)]
    [InlineData(50, 0x05, 0x00, 0x00)]
    [InlineData(125, 0x12, 0x50, 0x00)]
    [InlineData(-125, 0x12, 0x50, 0x01)]
    [InlineData(200, 0x20, 0x00, 0x00)]
    [InlineData(-200, 0x20, 0x00, 0x01)]
    public void ReferenceLevelMatchesIcomBcdFormat(
      int tenthsDb,
      byte integerBcd,
      byte decimalBcd,
      byte sign)
    {
      byte[] encoded =
        IcomScopeProtocol.EncodeReferenceLevel(
          tenthsDb);

      Assert.Equal(
        new[]
        {
          integerBcd,
          decimalBcd,
          sign
        },
        encoded);

      Assert.Equal(
        tenthsDb,
        IcomScopeProtocol.DecodeReferenceLevel(
          encoded));
    }

    [Fact]
    public void WriteModeBuildsCanonicalCivFrame()
    {
      byte[] frame =
        IcomScopeProtocol.BuildSet(
          IcomScopeReceiver.Sub,
          IcomScopeProtocol.ModeSubcommand,
          IcomScopeProtocol.EncodeMode(
            IcomScopeMode.ScrollFixed));

      Assert.Equal(
        new byte[]
        {
          0xFE, 0xFE, 0xA2, 0xE0,
          0x27, 0x14, 0x01, 0x03,
          0xFD
        },
        frame);
    }

    [Fact]
    public void QueryReplyExtractionRejectsWrongReceiver()
    {
      byte[] reply =
      [
        0xFE, 0xFE, 0xE0, 0xA2,
        0x27, 0x14, 0x01, 0x02,
        0xFD
      ];

      Assert.False(
        IcomScopeProtocol.TryExtractQueryReply(
          reply,
          IcomScopeReceiver.Main,
          IcomScopeProtocol.ModeSubcommand,
          1,
          out _));

      Assert.True(
        IcomScopeProtocol.TryExtractQueryReply(
          reply,
          IcomScopeReceiver.Sub,
          IcomScopeProtocol.ModeSubcommand,
          1,
          out byte[] value));

      Assert.Equal(
        new byte[] { 0x02 },
        value);
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(20_000)]
    [InlineData(1_000_000)]
    public void UnsupportedSpanIsRejected(
      long span)
    {
      Assert.Throws<ArgumentOutOfRangeException>(
        () =>
          IcomScopeProtocol.EncodeSpan(span));
    }

    [Theory]
    [InlineData(-205)]
    [InlineData(205)]
    [InlineData(123)]
    public void InvalidReferenceLevelIsRejected(
      int tenthsDb)
    {
      Assert.Throws<ArgumentOutOfRangeException>(
        () =>
          IcomScopeProtocol.EncodeReferenceLevel(
            tenthsDb));
    }
  }
}
