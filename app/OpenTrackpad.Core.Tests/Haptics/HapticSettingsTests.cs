using OpenTrackpad.Core.Haptics;

namespace OpenTrackpad.Core.Tests.Haptics;

public sealed class HapticSettingsTests
{
    [Theory]
    [InlineData(HapticPreset.Off, 0u)]
    [InlineData(HapticPreset.Light, 0x040415u)]
    [InlineData(HapticPreset.Medium, 0x060617u)]
    [InlineData(HapticPreset.Firm, 0x08081Eu)]
    public void FromPreset_UsesDocumentedStrength(HapticPreset preset, uint expected)
    {
        Assert.Equal(expected, HapticSettings.FromPreset(preset).ClickStrength);
    }

    [Fact]
    public void Default_UsesFirmClickAndRelease()
    {
        Assert.Equal(0x08081Eu, HapticSettings.Default.ClickStrength);
        Assert.Equal(0x020218u, HapticSettings.Default.ReleaseStrength);
    }

    [Fact]
    public void CustomStrength_Validates24BitRange()
    {
        var settings = HapticSettings.Default.WithClickStrength(0x123456);
        Assert.Equal(0x123456u, settings.ClickStrength);

        Assert.Throws<ArgumentOutOfRangeException>(() => settings.WithClickStrength(0x1000000));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.WithReleaseStrength(0x1000000));
    }
}
