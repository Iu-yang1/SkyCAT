using System;
using SkyCat;

namespace SkyCat.Tests
{
  // covers the radio-catalog surface of CatCommandSender that does not require an open serial port
  public class CatCommandSenderTests
  {
    private static CatCommandSender NewSender() => new();


    [Fact]
    public void LoadsAllShippedRadios()
    {
      var sender = NewSender();
      Assert.NotEmpty(sender.RadioNames);
      Assert.Contains("TS-2000", sender.RadioNames);
      Assert.Contains("IC-9700", sender.RadioNames);
    }

    [Fact]
    public void GetModelsMapsIdToName()
    {
      var models = NewSender().GetModels();
      Assert.Equal("TS-2000", models[2014]);
    }

    [Fact]
    public void EveryRadioHasAUniqueId()
    {
      // GetModels builds an id-keyed dictionary and throws on a duplicate id, so this guards against
      // two rig files colliding (as IC-705 and IC-705-wireless once did on id 3085)
      var sender = NewSender();
      var models = sender.GetModels();
      Assert.Equal(sender.RadioNames.Length, models.Count);
    }

    [Fact]
    public void GetRadioNameResolvesByIdAndByName()
    {
      var sender = NewSender();
      Assert.Equal("TS-2000", sender.GetRadioName("2014"));
      Assert.Equal("TS-2000", sender.GetRadioName("TS-2000"));
    }

    [Fact]
    public void GetRadioNameThrowsForUnknownModel()
    {
      Assert.Throws<ArgumentException>(() => NewSender().GetRadioName("does-not-exist"));
    }

    [Fact]
    public void SelectRadioSetsRadioNameAndCommandSet()
    {
      var sender = NewSender();
      sender.SelectRadio("IC-9700");
      Assert.Equal("IC-9700", sender.RadioName);
      Assert.NotNull(sender.CommandSet);
    }

    [Fact]
    public void ListModelsIncludesKnownRadio()
    {
      Assert.Contains("TS-2000", NewSender().ListModels());
    }

    [Fact]
    public void ListCapabilitiesReturnsJsonForSelectedRadio()
    {
      var json = NewSender().ListCapabilities("IC-9700");
      Assert.Contains("\"model\":\"IC-9700\"", json);
    }

    [Fact]
    public void ListAllCapabilitiesReturnsJsonArray()
    {
      var json = NewSender().ListAllCapabilities();
      Assert.StartsWith("[", json);
      Assert.Contains("TS-2000", json);
    }

    [Theory]
    [InlineData(new byte[] { 0x00 })]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00 })]
    public void ZeroOnlyTransportPaddingIsBenign(byte[] bytes)
    {
      Assert.True(CatCommandSender.IsZeroPadding(bytes));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x00, 0xFE, 0x00 })]
    [InlineData(new byte[] { 0xFD })]
    public void NonZeroOrEmptyTransportDataIsNotZeroPadding(byte[] bytes)
    {
      Assert.False(CatCommandSender.IsZeroPadding(bytes));
    }


    [Fact]
    public void CommandsAreUnavailableBeforeSetup()
    {
      // ListAvailableCommands has not run yet, so nothing is available
      Assert.False(NewSender().IsCommandAvailable(CatCommand.read_rx_frequency));
    }
  }
}
