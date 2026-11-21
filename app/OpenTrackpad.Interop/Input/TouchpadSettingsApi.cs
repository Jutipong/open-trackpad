using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OpenTrackpad.Interop.Input;

/// <summary>
/// Windows 11 24H2+ Precision Touchpad settings via
/// SystemParametersInfo(SPI_GET/SETTOUCHPADPARAMETERS).
/// </summary>
public static class TouchpadSettingsApi
{
    private const uint SpiGetTouchpadParameters = 0x00AE;
    private const uint SpiSetTouchpadParameters = 0x00AF;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    private const uint Flag2AllowActiveWhenMousePresent = 1u << 0;
    private const uint Flag2FeedbackEnabled = 1u << 1;
    private const uint Flag2TapEnabled = 1u << 2;
    private const uint Flag2TapAndDragEnabled = 1u << 3;
    private const uint Flag2TwoFingerTapEnabled = 1u << 4;
    private const uint Flag2RightClickZoneEnabled = 1u << 5;
    private const uint Flag2MouseAccelSettingHonored = 1u << 6;
    private const uint Flag2PanEnabled = 1u << 7;
    private const uint Flag2ZoomEnabled = 1u << 8;
    private const uint Flag2ScrollDirectionReversed = 1u << 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeTouchpadParameters
    {
        public uint versionNumber;
        public uint maxSupportedContacts;
        public uint legacyTouchpadFeatures;
        public uint flags1;
        public uint flags2;
        public uint sensitivityLevel;
        public uint cursorSpeed;
        public uint feedbackIntensity;
        public uint clickForceSensitivity;
        public uint rightClickZoneWidth;
        public uint rightClickZoneHeight;
    }

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool SystemParametersInfoW(
        uint uiAction, uint uiParam, ref NativeTouchpadParameters pvParam, uint fWinIni);

    public sealed class TouchpadParameters
    {
        public uint VersionNumber { get; set; } = 1;
        public uint MaxSupportedContacts { get; set; }
        public uint LegacyTouchpadFeatures { get; set; }
        public uint Flags1 { get; set; }
        public uint Flags2 { get; set; }
        public uint SensitivityLevel { get; set; }
        public uint CursorSpeed { get; set; }
        public uint FeedbackIntensity { get; set; }
        public uint ClickForceSensitivity { get; set; }
        public uint RightClickZoneWidth { get; set; }
        public uint RightClickZoneHeight { get; set; }

        public bool TouchpadPresent => (Flags1 & (1u << 0)) != 0;
        public bool FeedbackSupported => (Flags1 & (1u << 5)) != 0;
        public bool ClickForceSupported => (Flags1 & (1u << 6)) != 0;

        public bool FeedbackEnabled
        {
            get => HasFlag2(Flag2FeedbackEnabled);
            set => SetFlag2(Flag2FeedbackEnabled, value);
        }

        public bool TapEnabled
        {
            get => HasFlag2(Flag2TapEnabled);
            set => SetFlag2(Flag2TapEnabled, value);
        }

        public bool TapAndDragEnabled
        {
            get => HasFlag2(Flag2TapAndDragEnabled);
            set => SetFlag2(Flag2TapAndDragEnabled, value);
        }

        public bool AllowActiveWhenMousePresent
        {
            get => HasFlag2(Flag2AllowActiveWhenMousePresent);
            set => SetFlag2(Flag2AllowActiveWhenMousePresent, value);
        }

        public bool TwoFingerTapEnabled
        {
            get => HasFlag2(Flag2TwoFingerTapEnabled);
            set => SetFlag2(Flag2TwoFingerTapEnabled, value);
        }

        public bool RightClickZoneEnabled
        {
            get => HasFlag2(Flag2RightClickZoneEnabled);
            set => SetFlag2(Flag2RightClickZoneEnabled, value);
        }

        public bool ZoomEnabled
        {
            get => HasFlag2(Flag2ZoomEnabled);
            set => SetFlag2(Flag2ZoomEnabled, value);
        }

        public bool ScrollDirectionReversed
        {
            get => HasFlag2(Flag2ScrollDirectionReversed);
            set => SetFlag2(Flag2ScrollDirectionReversed, value);
        }

        private bool HasFlag2(uint flag) => (Flags2 & flag) != 0;

        private void SetFlag2(uint flag, bool enabled)
        {
            if (enabled) Flags2 |= flag;
            else Flags2 &= ~flag;
        }
    }

    /// <summary>
    /// Returns null when the OS does not support SPI_GETTOUCHPADPARAMETERS
    /// or when no Precision Touchpad is present.
    /// </summary>
    public static TouchpadParameters? Get()
    {
        var native = new NativeTouchpadParameters { versionNumber = 1 };
        uint size = (uint)Marshal.SizeOf<NativeTouchpadParameters>();

        if (!SystemParametersInfoW(SpiGetTouchpadParameters, size, ref native, 0))
            return null;

        return new TouchpadParameters
        {
            VersionNumber = native.versionNumber,
            MaxSupportedContacts = native.maxSupportedContacts,
            LegacyTouchpadFeatures = native.legacyTouchpadFeatures,
            Flags1 = native.flags1,
            Flags2 = native.flags2,
            SensitivityLevel = native.sensitivityLevel,
            CursorSpeed = native.cursorSpeed,
            FeedbackIntensity = native.feedbackIntensity,
            ClickForceSensitivity = native.clickForceSensitivity,
            RightClickZoneWidth = native.rightClickZoneWidth,
            RightClickZoneHeight = native.rightClickZoneHeight,
        };
    }

    public static void Set(TouchpadParameters parameters)
    {
        var native = new NativeTouchpadParameters
        {
            versionNumber = 1,
            maxSupportedContacts = parameters.MaxSupportedContacts,
            legacyTouchpadFeatures = parameters.LegacyTouchpadFeatures,
            flags1 = parameters.Flags1,
            flags2 = parameters.Flags2,
            sensitivityLevel = parameters.SensitivityLevel,
            cursorSpeed = parameters.CursorSpeed,
            feedbackIntensity = parameters.FeedbackIntensity,
            clickForceSensitivity = parameters.ClickForceSensitivity,
            rightClickZoneWidth = parameters.RightClickZoneWidth,
            rightClickZoneHeight = parameters.RightClickZoneHeight,
        };

        uint size = (uint)Marshal.SizeOf<NativeTouchpadParameters>();
        if (!SystemParametersInfoW(
                SpiSetTouchpadParameters, size, ref native,
                SpifUpdateIniFile | SpifSendChange))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "SystemParametersInfo(SPI_SETTOUCHPADPARAMETERS) failed");
        }
    }

    public static void Update(Action<TouchpadParameters> mutate)
    {
        TouchpadParameters parameters = Get()
            ?? throw new InvalidOperationException(
                "Precision Touchpad settings API is not available.");
        mutate(parameters);
        Set(parameters);
    }
}
