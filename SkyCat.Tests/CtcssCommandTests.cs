using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SkyCat;
using static SkyCat.CatCommandSet;

namespace SkyCat.Tests
{
  // covers the CTCSS-tone feature added to SkyCat: the CatCommand enum members and the IC-9700
  // command bytes, including the full on-air frame produced for a 67.0 Hz tone
  public class CtcssCommandTests
  {
    private static readonly CatCommandSender Sender = new();
    private static readonly MethodInfo ParamToBytesMethod =
      typeof(CatCommandSender).GetMethod("ParamToBytes", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static CatCommandSet Ic9700()
    {
      var path = Path.Combine(AppContext.BaseDirectory, "Rigs", "IC-9700.json");
      return CatCommandSet.FromJson(File.ReadAllText(path));
    }

    // rebuilds a command message's on-air bytes the same way CatCommandSender.SendMessage does,
    // so a param value can be spliced into the null block
    private static byte[] BuildFrame(CatMessage message, string paramValue)
    {
      var bytes = message.Command.Select(b => b ?? (byte)0).ToArray();
      if (message.CommandParam != null)
      {
        int count = message.Command.Count(b => b == null);
        int start = Array.IndexOf(message.Command, null);
        var paramBytes = (byte[])ParamToBytesMethod.Invoke(Sender, new object?[] { message.CommandParam, paramValue, count })!;
        Array.Copy(paramBytes, 0, bytes, start, paramBytes.Length);
      }
      return bytes;
    }




    //----------------------------------------------------------------------------------------------
    //                                        enum members
    //----------------------------------------------------------------------------------------------
    [Theory]
    [InlineData("write_ctcss_tone")]
    [InlineData("enable_ctcss")]
    [InlineData("disable_ctcss")]
    public void CatCommandDefinesToneMembers(string name)
    {
      Assert.True(Enum.TryParse<CatCommand>(name, out _));
    }




    //----------------------------------------------------------------------------------------------
    //                                     IC-9700 definitions
    //----------------------------------------------------------------------------------------------
    [Theory]
    [InlineData(CatCommand.write_ctcss_tone)]
    [InlineData(CatCommand.enable_ctcss)]
    [InlineData(CatCommand.disable_ctcss)]
    public void Ic9700DuplexAndSimplexDefineToneCommands(CatCommand command)
    {
      var commandSet = Ic9700();
      Assert.True(commandSet.Duplex!.ContainsKey(command));
      Assert.True(commandSet.Simplex!.ContainsKey(command));
    }

    [Theory]
    [InlineData(CatCommand.write_ctcss_tone)]
    [InlineData(CatCommand.enable_ctcss)]
    [InlineData(CatCommand.disable_ctcss)]
    public void ToneCommandsAreUnrestricted(CatCommand command)
    {
      // "none" keeps them available in the pre-PTT (receiving) window where SkyRoof issues them
      Assert.Equal(CatRestriction.none, Ic9700().Simplex![command].Restriction);
    }

    [Fact]
    public void Ic9700DuplexToneSelectsSubThenRestoresMainReceiver()
    {
      // In SAT mode the uplink is on SUB, but the operator's audible downlink is
      // MAIN. Apply the tone to SUB and always restore MAIN focus afterwards.
      var info = Ic9700().Duplex![CatCommand.write_ctcss_tone];
      Assert.Equal(3, info.Messages.Length);
      Assert.Equal(
        new byte?[] { 0xFE, 0xFE, 0xA2, 0xE0, 0x07, 0xD2, 0x01, 0xFD },
        info.Messages[0].Command);
      Assert.Equal(
        new byte?[] { 0xFE, 0xFE, 0xA2, 0xE0, 0x07, 0xD2, 0x00, 0xFD },
        info.Messages[^1].Command);
    }




    //----------------------------------------------------------------------------------------------
    //                                    on-air byte frames
    //----------------------------------------------------------------------------------------------
    [Theory]
    [InlineData("670", new byte[] { 0x00, 0x06, 0x70 })]   // 67.0 Hz
    [InlineData("744", new byte[] { 0x00, 0x07, 0x44 })]   // 74.4 Hz (SO-50 arming)
    [InlineData("1413", new byte[] { 0x00, 0x14, 0x13 })]  // 141.3 Hz
    public void SimplexWriteToneProducesRepeaterToneFrame(string toneTenthsHz, byte[] bcd)
    {
      // full CI-V frame: FE FE A2 E0 1B 00 <3-byte BCD tone> FD
      var message = Ic9700().Simplex![CatCommand.write_ctcss_tone].Messages[0];
      var expected = new byte[] { 0xFE, 0xFE, 0xA2, 0xE0, 0x1B, 0x00 }
        .Concat(bcd).Concat(new byte[] { 0xFD }).ToArray();
      Assert.Equal(expected, BuildFrame(message, toneTenthsHz));
    }

    [Fact]
    public void EnableAndDisableUseRepeaterToneFunction()
    {
      var simplex = Ic9700().Simplex!;
      Assert.Equal(
        new byte?[] { 0xFE, 0xFE, 0xA2, 0xE0, 0x16, 0x42, 0x01, 0xFD },
        simplex[CatCommand.enable_ctcss].Messages[0].Command);
      Assert.Equal(
        new byte?[] { 0xFE, 0xFE, 0xA2, 0xE0, 0x16, 0x42, 0x00, 0xFD },
        simplex[CatCommand.disable_ctcss].Messages[0].Command);
    }




    //----------------------------------------------------------------------------------------------
    //                              every rig that defines the tone commands
    //----------------------------------------------------------------------------------------------
    // the 38 tones, in tenths of Hz, that every radio with a CAT tone command can produce. this is
    // the set SkyRoof offers: the TS-2000 has neither 69.3 nor any of the 11 extended tones
    // (159.8 .. 254.1), and the FT-847 has no extended tones either
    private static readonly string[] StandardTones =
    {
      "670", "719", "744", "770", "797", "825", "854", "885", "915", "948",
      "974", "1000", "1035", "1072", "1109", "1148", "1188", "1230", "1273", "1318",
      "1365", "1413", "1462", "1514", "1567", "1622", "1679", "1738", "1799", "1862",
      "1928", "2035", "2107", "2181", "2257", "2336", "2418", "2503"
    };

    public static IEnumerable<object[]> RigFiles()
    {
      var dir = Path.Combine(AppContext.BaseDirectory, "Rigs");
      foreach (var file in Directory.GetFiles(dir, "*.json"))
        yield return new object[] { Path.GetFileName(file) };
    }

    [Theory]
    [MemberData(nameof(RigFiles))]
    public void RigsThatDefineToneAreCompleteAndEncodeStandardTones(string fileName)
    {
      var path = Path.Combine(AppContext.BaseDirectory, "Rigs", fileName);
      var commandSet = CatCommandSet.FromJson(File.ReadAllText(path));

      var groups = new[] { commandSet.Duplex, commandSet.Split, commandSet.Simplex, commandSet.Transmitter, commandSet.Receiver };
      foreach (var group in groups)
      {
        if (group == null || !group.ContainsKey(CatCommand.write_ctcss_tone)) continue;

        // a rig that can set the tone must also be able to enable and disable it
        Assert.True(group.ContainsKey(CatCommand.enable_ctcss), $"{fileName}: enable_ctcss missing");
        Assert.True(group.ContainsKey(CatCommand.disable_ctcss), $"{fileName}: disable_ctcss missing");

        // the parameterised message must encode every standard tone without throwing (catches gaps
        // in the per-rig Enum tables and any BCD/format mistakes)
        var message = group[CatCommand.write_ctcss_tone].Messages.First(m => m.CommandParam != null);
        foreach (var tone in StandardTones)
          Assert.NotEmpty(BuildFrame(message, tone));

        // and it must offer nothing beyond them, so that every rig behaves the same way
        var values = message.CommandParam!.Values;
        if (values != null)
          Assert.Equal(StandardTones.OrderBy(t => t), values.Keys.OrderBy(t => t));
      }
    }
  }
}
