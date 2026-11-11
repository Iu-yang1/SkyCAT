using System;
using System.Linq;
using SkyCat;

namespace SkyCat.Tests
{
  public sealed class Icom905ExtendedFrequencyTests
  {
    [Theory]
    [InlineData("144000000", false)]
    [InlineData("5760000000", false)]
    [InlineData("9999999999", false)]
    [InlineData("10368100000", true)]
    [InlineData("10500000000", true)]
    public void FrequencyWidthFollowsIcom905OperatingBand(
      string hertz, bool sixBytes)
    {
      Assert.Equal(sixBytes,
        Icom905FrequencyFrame.RequiresSixBytes(hertz));
    }

    [Fact]
    public void ExtendedCommandPreservesSelectedVfoSelectorAndTail()
    {
      byte?[] template = {
        0xFE, 0xFE, 0xAC, 0xE0, 0x25, 0x01,
        null, null, null, null, null, 0xFD
      };
      byte[] frequency = { 0x00, 0x00, 0x10, 0x68, 0x03, 0x01 };
      byte[] expected = {
        0xFE, 0xFE, 0xAC, 0xE0, 0x25, 0x01,
        0x00, 0x00, 0x10, 0x68, 0x03, 0x01, 0xFD
      };

      Assert.Equal(expected,
        Icom905FrequencyFrame.ComposeCommand(template, frequency));
      Assert.Equal(template.Length, 
        Icom905FrequencyFrame.ComposeCommand(template,
          frequency[..5]).Length);
    }

    [Fact]
    public void ExtendedReplyMustMatchAddressVfoAndFrequencyLength()
    {
      byte?[] template = {
        0xFE, 0xFE, 0xE0, 0xAC, 0x25, 0x01,
        null, null, null, null, null, 0xFD
      };
      byte[] response = {
        0xFE, 0xFE, 0xE0, 0xAC, 0x25, 0x01,
        0x00, 0x00, 0x10, 0x68, 0x03, 0x01, 0xFD
      };
      Assert.True(Icom905FrequencyFrame.MatchesSixByteReply(
        response, template, out int start));
      Assert.Equal(6, start);

      byte[] wrongSelector = (byte[])response.Clone();
      wrongSelector[5] = 0x00;
      Assert.False(Icom905FrequencyFrame.MatchesSixByteReply(
        wrongSelector, template, out _));

      byte[] invalidBcd = (byte[])response.Clone();
      invalidBcd[10] = 0xFA;
      Assert.False(Icom905FrequencyFrame.MatchesSixByteReply(
        invalidBcd, template, out _));

      byte[] wrongLength = response[..^2];
      Assert.False(Icom905FrequencyFrame.MatchesSixByteReply(
        wrongLength, template, out _));
    }

    [Fact]
    public void Ic905RigCatalogExplicitlyMarksEveryFrequencyCommand()
    {
      var sender = new CatCommandSender();
      sender.SelectRadio("IC-905");
      Assert.NotNull(sender.CommandSet);

      foreach (CatCommandSet.CatCommandGroup? group in new[] {
        sender.CommandSet!.Split, sender.CommandSet.Simplex })
      {
        Assert.NotNull(group);
        foreach (CatCommand action in new[] {
          CatCommand.read_rx_frequency, CatCommand.read_tx_frequency,
          CatCommand.write_rx_frequency, CatCommand.write_tx_frequency })
        {
          Assert.True(group.TryGetValue(action, out var command));
          // Only command 25 carries a frequency payload. Subsequent
          // split ON/OFF maintenance messages must keep fixed framing.
          Assert.True(command!.Messages[0].VariableFrequencyLength);
          Assert.All(command.Messages.Skip(1),
            message => Assert.False(message.VariableFrequencyLength));
        }
      }

      sender.SelectRadio("IC-705");
      Assert.NotNull(sender.CommandSet);
      Assert.False(sender.CommandSet!.Split![
        CatCommand.write_rx_frequency].Messages[0].VariableFrequencyLength);
    }

    [Theory]
    [InlineData("IC-705", 0xA4)]
    [InlineData("IC-705-wireless", 0xA4)]
    [InlineData("IC-905", 0xAC)]
    public void SingleScopeModelsExposeNativeWaveformOutputControl(
      string model, int civAddress)
    {
      var sender = new CatCommandSender();
      sender.SelectRadio(model);
      Assert.NotNull(sender.CommandSet);

      foreach (CatCommandSet.CatCommandGroup? group in new[] {
        sender.CommandSet!.Simplex, sender.CommandSet.Split })
      {
        Assert.NotNull(group);
        foreach ((CatCommand command, byte selector, byte value) in new[] {
          (CatCommand.enable_scope, (byte)0x10, (byte)0x01),
          (CatCommand.disable_scope, (byte)0x10, (byte)0x00),
          (CatCommand.enable_scope_data, (byte)0x11, (byte)0x01),
          (CatCommand.disable_scope_data, (byte)0x11, (byte)0x00) })
        {
          Assert.True(group.TryGetValue(command, out var entry));
          Assert.Equal(new byte?[] {
            0xFE, 0xFE, (byte)civAddress, 0xE0,
            0x27, selector, value, 0xFD
          }, entry!.Messages[0].Command);
          Assert.Equal(new byte?[] {
            0xFE, 0xFE, 0xE0, (byte)civAddress, 0xFB, 0xFD
          }, entry.Messages[0].Reply);
        }
      }
    }

    [Fact]
    public void NoOtherCatCommandAdoptsExtendedFrequencyEncoding()
    {
      var sender = new CatCommandSender();
      sender.SelectRadio("IC-905");
      Assert.NotNull(sender.CommandSet);
      foreach (CatCommandSet.CatCommandGroup? group in new[] {
        sender.CommandSet!.Split, sender.CommandSet.Simplex })
      {
        foreach (var pair in group!)
        {
          if (pair.Key is CatCommand.read_rx_frequency
            or CatCommand.read_tx_frequency
            or CatCommand.write_rx_frequency
            or CatCommand.write_tx_frequency)
            continue;

          Assert.All(pair.Value.Messages,
            message => Assert.False(message.VariableFrequencyLength));
        }
      }
    }
  }
}
