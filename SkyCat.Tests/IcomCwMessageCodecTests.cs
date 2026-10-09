using System.Text;
using SkyCat;

namespace SkyCat.Tests
{
  public sealed class IcomCwMessageCodecTests
  {
    [Fact]
    public void BuildSendFrame_EncodesDocumentedCommand17Payload()
    {
      byte[] frame =
        IcomCwMessageCodec.BuildSendFrame(
          "CQ DE K1ABC");

      Assert.Equal(
        new byte[]
        {
          0xFE, 0xFE,
          0xA2, 0xE0,
          0x17,
          0x43, 0x51, 0x20,
          0x44, 0x45, 0x20,
          0x4B, 0x31, 0x41,
          0x42, 0x43,
          0xFD
        },
        frame);
    }

    [Fact]
    public void AbortFrame_UsesDocumentedFfSentinel()
    {
      Assert.Equal(
        new byte[]
        {
          0xFE, 0xFE,
          0xA2, 0xE0,
          0x17, 0xFF,
          0xFD
        },
        IcomCwMessageCodec.BuildAbortFrame());
    }

    [Fact]
    public void ThirtyCharactersAcceptedButThirtyOneRejected()
    {
      string max =
        new('A', 30);

      Assert.Equal(
        36,
        IcomCwMessageCodec
          .BuildSendFrame(max)
          .Length);

      Assert.Throws<ArgumentException>(() =>
        IcomCwMessageCodec.BuildSendFrame(
          max + "B"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("CQ_DE")]
    [InlineData("CQ;DE")]
    [InlineData("CQ\nDE")]
    [InlineData("CQ 你好")]
    public void UnsupportedPayloadsAreRejected(
      string text)
    {
      Assert.Throws<ArgumentException>(() =>
        IcomCwMessageCodec.BuildSendFrame(
          text));
    }

    [Fact]
    public void Base64TransportRoundTripsSpacesAndPunctuation()
    {
      const string text =
        "CQ DE K1ABC/7? TEST.";

      string encoded =
        IcomCwMessageCodec
          .EncodeBase64Payload(text);

      Assert.Equal(
        text,
        IcomCwMessageCodec
          .DecodeBase64Payload(encoded));
      Assert.DoesNotContain(
        ' ',
        encoded);
    }

    [Fact]
    public void InvalidBase64IsRejectedBeforeRadioDispatch()
    {
      Assert.Throws<ArgumentException>(() =>
        IcomCwMessageCodec
          .DecodeBase64Payload("***"));
    }

    [Fact]
    public void Base64DecodedUnsupportedByteIsRejected()
    {
      string encoded =
        Convert.ToBase64String(
          new byte[] { 0x43, 0x51, 0x5F });

      Assert.Throws<ArgumentException>(() =>
        IcomCwMessageCodec
          .DecodeBase64Payload(encoded));
    }
  }
}
