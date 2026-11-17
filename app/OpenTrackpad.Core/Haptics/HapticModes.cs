namespace OpenTrackpad.Core.Haptics;

public enum HapticMode
{
    Preset,
    Quiet,
    Maximum,
    Disabled,
}

public sealed record HapticModeValues(HapticSettings Strengths, bool ButtonDisabled);

public static class HapticModes
{
    public const uint MaximumStrength = 0xFFFFFF;

    public static HapticSettings Maximum { get; } =
        new(MaximumStrength, MaximumStrength);

    public static HapticModeValues Resolve(HapticMode mode, HapticSettings preset) => mode switch
    {
        HapticMode.Preset => new HapticModeValues(preset, ButtonDisabled: false),
        HapticMode.Quiet => new HapticModeValues(new HapticSettings(0, 0), ButtonDisabled: false),
        HapticMode.Maximum => new HapticModeValues(Maximum, ButtonDisabled: false),
        HapticMode.Disabled => new HapticModeValues(new HapticSettings(0, 0), ButtonDisabled: true),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static HapticMode Detect(uint clickStrength, uint releaseStrength, bool buttonDisabled)
    {
        if (buttonDisabled)
            return HapticMode.Disabled;
        if (clickStrength == 0 && releaseStrength == 0)
            return HapticMode.Quiet;
        if (clickStrength == MaximumStrength && releaseStrength == MaximumStrength)
            return HapticMode.Maximum;
        return HapticMode.Preset;
    }
}
