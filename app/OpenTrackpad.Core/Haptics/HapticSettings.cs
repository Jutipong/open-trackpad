namespace OpenTrackpad.Core.Haptics;

public enum HapticPreset
{
    Off,
    Light,
    Medium,
    Firm,
}

public sealed record HapticSettings(uint ClickStrength, uint ReleaseStrength)
{
    public const uint LightStrength = 0x040415;
    public const uint MediumStrength = 0x060617;
    public const uint FirmStrength = 0x08081E;
    public const uint DefaultReleaseStrength = 0x020218;

    public static HapticSettings Default { get; } =
        new(FirmStrength, DefaultReleaseStrength);

    public static HapticSettings FromPreset(HapticPreset preset) => preset switch
    {
        HapticPreset.Off => new HapticSettings(0, 0),
        HapticPreset.Light => new HapticSettings(LightStrength, DefaultReleaseStrength),
        HapticPreset.Medium => new HapticSettings(MediumStrength, DefaultReleaseStrength),
        HapticPreset.Firm => new HapticSettings(FirmStrength, DefaultReleaseStrength),
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };

    public static bool IsValidStrength(uint strength) => strength <= 0xFFFFFF;

    public HapticSettings WithClickStrength(uint strength)
    {
        if (!IsValidStrength(strength))
            throw new ArgumentOutOfRangeException(nameof(strength), "Haptic strength is 24-bit.");
        return this with { ClickStrength = strength };
    }

    public HapticSettings WithReleaseStrength(uint strength)
    {
        if (!IsValidStrength(strength))
            throw new ArgumentOutOfRangeException(nameof(strength), "Haptic strength is 24-bit.");
        return this with { ReleaseStrength = strength };
    }
}
