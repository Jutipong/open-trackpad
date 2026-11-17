using OpenTrackpad.Core.Haptics;

namespace OpenTrackpad.Core.Tests.Haptics;

public sealed class HapticModesTests
{
    [Fact]
    public void Resolve_PresetKeepsPresetAndEnablesButton()
    {
        HapticModeValues values = HapticModes.Resolve(HapticMode.Preset, HapticSettings.Default);

        Assert.Equal(HapticSettings.Default, values.Strengths);
        Assert.False(values.ButtonDisabled);
    }

    [Fact]
    public void Resolve_QuietZerosFeedbackButKeepsButton()
    {
        HapticModeValues values = HapticModes.Resolve(HapticMode.Quiet, HapticSettings.Default);

        Assert.Equal(new HapticSettings(0, 0), values.Strengths);
        Assert.False(values.ButtonDisabled);
    }

    [Fact]
    public void Resolve_MaximumUsesAllBits()
    {
        HapticModeValues values = HapticModes.Resolve(HapticMode.Maximum, HapticSettings.Default);

        Assert.Equal(0xFFFFFFu, values.Strengths.ClickStrength);
        Assert.Equal(0xFFFFFFu, values.Strengths.ReleaseStrength);
        Assert.False(values.ButtonDisabled);
    }

    [Fact]
    public void Resolve_DisabledZerosFeedbackAndDisablesButton()
    {
        HapticModeValues values = HapticModes.Resolve(HapticMode.Disabled, HapticSettings.Default);

        Assert.Equal(new HapticSettings(0, 0), values.Strengths);
        Assert.True(values.ButtonDisabled);
    }

    [Theory]
    [InlineData(0x08081Eu, 0x020218u, false, HapticMode.Preset)]
    [InlineData(0u, 0u, false, HapticMode.Quiet)]
    [InlineData(0xFFFFFFu, 0xFFFFFFu, false, HapticMode.Maximum)]
    [InlineData(0u, 0u, true, HapticMode.Disabled)]
    public void Detect_ReadsStateFromDriverValues(
        uint click, uint release, bool buttonDisabled, HapticMode expected)
    {
        Assert.Equal(expected, HapticModes.Detect(click, release, buttonDisabled));
    }
}
