using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using OpenTrackpad.Core.Haptics;
using OpenTrackpad.Interop.Driver;

namespace OpenTrackpad.App;

public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private readonly DispatcherTimer _clickApplyTimer;
    private readonly DispatcherTimer _scrollApplyTimer;
    private readonly DispatcherTimer _pointerApplyTimer;
    private BatteryReading? _lastBatteryReading;
    private string? _lastBatteryError;
    private bool _allowClose;
    private bool _loading = true;

    public MainWindow(AppHost host)
    {
        InitializeComponent();
        _host = host;

        _clickApplyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _clickApplyTimer.Tick += (_, _) =>
        {
            _clickApplyTimer.Stop();
            ApplyClickFromSlider();
        };

        _scrollApplyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _scrollApplyTimer.Tick += (_, _) =>
        {
            _scrollApplyTimer.Stop();
            ApplyScrollFromSlider();
        };

        _pointerApplyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _pointerApplyTimer.Tick += (_, _) =>
        {
            _pointerApplyTimer.Stop();
            ApplyPointerFromControls();
        };

        AdminText.Text = Loc.T(host.IsAdministrator ? "Header.Admin" : "Header.User");
        RestartAdminButton.Visibility = host.IsAdministrator ? Visibility.Collapsed : Visibility.Visible;
        LanguageComboBox.SelectedIndex = Loc.Language == "en" ? 1 : 0;

        LoadDriverSettings();
        CursorSpeedSlider.Value = host.GetCursorSpeed();
        CursorSpeedValueText.Text = ((int)CursorSpeedSlider.Value).ToString();
        TapToClickCheckBox.IsChecked = host.IsTapToClickEnabled();
        SecondaryClickCheckBox.IsChecked = host.GetSecondaryClickMode() == 1;
        NaturalScrollCheckBox.IsChecked = host.IsNaturalScrolling();
        ZoomCheckBox.IsChecked = host.IsZoomEnabled();
        PanSensitivitySlider.Value = host.ReadScrollSensitivity();
        PanSensitivityValueText.Text = ((int)Math.Round(PanSensitivitySlider.Value)).ToString();
        StartWithWindowsCheckBox.IsChecked = StartupManager.IsEnabled();
        DragCheckBox.IsChecked = host.Drag.IsEnabled;
        DragStatusText.Text = Loc.T(host.Drag.IsEnabled ? "Drag.On" : "Drag.Off");
        DragGainSlider.Value = host.GetDragGain();
        DragGainValueText.Text = host.GetDragGain().ToString("0.00", CultureInfo.InvariantCulture);

        _loading = false;
    }

    public event Action? HiddenToTray;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            int enabled = 1;
            if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        }
        catch
        {
            // Dark title bar is cosmetic and unsupported on older Windows builds.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public void SetBattery(BatteryReading reading)
    {
        _lastBatteryReading = reading;
        _lastBatteryError = null;
        BatteryText.Text = $"{reading.Level}%";
        BatteryText.ToolTip = null;
        StatusText.Text = reading.Transport switch
        {
            TrackpadTransport.Bluetooth => Loc.T("Status.Bluetooth"),
            TrackpadTransport.Usb => Loc.T("Status.Usb"),
            _ => Loc.T("Status.NoDevice"),
        };
    }

    public void SetBatteryError(string error)
    {
        _lastBatteryReading = null;
        _lastBatteryError = error;
        BatteryText.Text = Loc.T("Status.BatteryUnavailable");
        BatteryText.ToolTip = error;
        StatusText.Text = _host.GetTransport() switch
        {
            TrackpadTransport.Bluetooth => Loc.T("Status.BluetoothNoBattery"),
            TrackpadTransport.Usb => Loc.T("Status.UsbNoBattery"),
            _ => Loc.T("Status.NoDeviceBattery"),
        };
    }

    public void RefreshLocalizedText()
    {
        AdminText.Text = Loc.T(_host.IsAdministrator ? "Header.Admin" : "Header.User");
        DragStatusText.Text = Loc.T(_host.Drag.IsEnabled ? "Drag.On" : "Drag.Off");
        ScrollStatusText.Text = string.Empty;
        PointerStatusText.Text = string.Empty;
        if (_lastBatteryError is not null)
            SetBatteryError(_lastBatteryError);
        else if (_lastBatteryReading is { } reading)
            SetBattery(reading);
        LoadDriverSettings();
    }

    public void SetDragStatus(string status) => DragStatusText.Text = status;

    public void SetDragChecked(bool enabled)
    {
        if (DragCheckBox.IsChecked == enabled) return;
        bool wasLoading = _loading;
        _loading = true;
        DragCheckBox.IsChecked = enabled;
        _loading = wasLoading;
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    private void LoadDriverSettings()
    {
        bool wasLoading = _loading;
        _loading = true;

        DriverSettings settings = DriverSettingsStore.Read();
        StopPressureBox.Text = settings.StopPressure.ToString(CultureInfo.InvariantCulture);
        StopSizeBox.Text = settings.StopSize.ToString(CultureInfo.InvariantCulture);
        IgnoreNearFingersCheck.IsChecked = settings.IgnoreNearFingers;
        PalmRejectionCheck.IsChecked = settings.PalmRejection;
        IgnoreButtonFingerCheck.IsChecked = settings.IgnoreButtonFinger;

        HapticMode mode = HapticModes.Detect(
            settings.FeedbackClick, settings.FeedbackRelease, settings.ButtonDisabled);
        HapticSettings? backup = _host.Settings.QuietClickBackup;
        uint sliderStrength = mode == HapticMode.Preset
            ? settings.FeedbackClick
            : backup?.ClickStrength ?? HapticSettings.FirmStrength;

        ClickStrengthSlider.Value = IndexFromStrength(sliderStrength);
        ClickStrengthValueText.Text = PresetNameFromIndex((int)ClickStrengthSlider.Value);
        ClickStrengthSlider.IsEnabled = mode == HapticMode.Preset;
        QuietClickCheckBox.IsChecked = mode == HapticMode.Quiet;
        MaximumFeedbackCheckBox.IsChecked = mode == HapticMode.Maximum;
        DisableFeedbackCheckBox.IsChecked = mode == HapticMode.Disabled;
        ClickStatusText.Text = mode switch
        {
            HapticMode.Quiet => Loc.T("Click.QuietOn"),
            HapticMode.Maximum => Loc.T("Click.MaxOn"),
            HapticMode.Disabled => Loc.T("Click.DisabledOn"),
            _ => string.Empty,
        };

        _loading = wasLoading;
    }

    private static int IndexFromStrength(uint strength) => strength switch
    {
        0 => 2,
        HapticSettings.LightStrength => 0,
        HapticSettings.MediumStrength => 1,
        HapticSettings.FirmStrength => 2,
        _ => 2,
    };

    private static HapticPreset PresetFromIndex(int index) => index switch
    {
        0 => HapticPreset.Light,
        1 => HapticPreset.Medium,
        _ => HapticPreset.Firm,
    };

    private static string PresetNameFromIndex(int index) => index switch
    {
        0 => "Light",
        1 => "Medium",
        _ => "Firm",
    };

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = Loc.T("Status.ReadingBattery");
        _host.RefreshBattery();
    }

    private void CursorSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CursorSpeedValueText is not null)
            CursorSpeedValueText.Text = ((int)Math.Round(e.NewValue)).ToString();

        if (_loading) return;

        try
        {
            _host.SetCursorSpeed((int)Math.Round(e.NewValue));
        }
        catch (Exception ex)
        {
            AppLog.Error("Tracking speed apply failed", ex);
            StatusText.Text = Loc.Format("Status.TrackingFailed", ex.Message);
        }
    }

    private void ClickStrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ClickStrengthValueText is not null)
            ClickStrengthValueText.Text = PresetNameFromIndex((int)Math.Round(e.NewValue));

        if (_loading) return;
        RestartTimer(_clickApplyTimer);
    }

    private void PreviewClick_Click(object sender, RoutedEventArgs e)
    {
        HapticPreset preset = PresetFromIndex((int)Math.Round(ClickStrengthSlider.Value));
        try
        {
            uint strength = HapticSettings.FromPreset(preset).ClickStrength;
            ControlPanelResponse response = _host.PreviewHaptic(strength);
            ClickStatusText.Text = response.Ok
                ? Loc.Format("Click.PreviewDone", PresetNameFromIndex((int)ClickStrengthSlider.Value))
                : Loc.Format("Click.PreviewStatus", response.Status);
        }
        catch (Exception ex)
        {
            AppLog.Error("Haptic preview failed", ex);
            ClickStatusText.Text = IsAccessDenied(ex)
                ? Loc.T("Click.PreviewDenied")
                : Loc.Format("Click.PreviewFailed", ex.Message);
        }
    }

    private static bool IsAccessDenied(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is Win32Exception { NativeErrorCode: 5 })
                return true;
        }

        return false;
    }

    private void ApplyClickFromSlider()
    {
        if (_loading) return;

        HapticPreset preset = PresetFromIndex((int)Math.Round(ClickStrengthSlider.Value));
        HapticSettings haptics = HapticSettings.FromPreset(preset);

        DriverSettings current = DriverSettingsStore.Read();
        if (current.FeedbackClick == haptics.ClickStrength &&
            current.FeedbackRelease == haptics.ReleaseStrength)
        {
            ClickStatusText.Text = Loc.Format(
                "Click.Already", PresetNameFromIndex((int)ClickStrengthSlider.Value));
            return;
        }

        ClickStatusText.Text = Loc.T("Click.Saving");
        try
        {
            _host.ApplyHaptics(haptics);
            LoadDriverSettings();
            AppLog.Info($"Click auto-applied: {preset}");
            ClickStatusText.Text = Loc.Format(
                "Click.Applied", PresetNameFromIndex((int)ClickStrengthSlider.Value));
        }
        catch (Exception ex)
        {
            AppLog.Error("Haptic auto-apply failed", ex);
            ClickStatusText.Text = Loc.Format("Click.SaveFailed", ex.Message);
            LoadDriverSettings();
        }
    }

    private void HapticModeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        _clickApplyTimer.Stop();
        var checkBox = (CheckBox)sender;
        HapticMode mode;
        if (checkBox.IsChecked != true)
            mode = HapticMode.Preset;
        else if (ReferenceEquals(checkBox, QuietClickCheckBox))
            mode = HapticMode.Quiet;
        else if (ReferenceEquals(checkBox, MaximumFeedbackCheckBox))
            mode = HapticMode.Maximum;
        else
            mode = HapticMode.Disabled;

        try
        {
            ClickStatusText.Text = Loc.T("Click.Saving");
            _host.SetHapticMode(mode);
            LoadDriverSettings();
            ClickStatusText.Text = mode switch
            {
                HapticMode.Quiet => Loc.T("Click.QuietOn"),
                HapticMode.Maximum => Loc.T("Click.MaxOn"),
                HapticMode.Disabled => Loc.T("Click.DisabledOn"),
                _ => Loc.T("Click.PresetOn"),
            };
        }
        catch (Exception ex)
        {
            AppLog.Error("Haptic mode change failed", ex);
            ClickStatusText.Text = Loc.Format("Click.ModeFailed", ex.Message);
            LoadDriverSettings();
        }
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        string language = LanguageComboBox.SelectedIndex == 1 ? "en" : "th";
        if (language == Loc.Language) return;

        _host.Settings.Language = language;
        AppSettingsStore.Save(_host.Settings);
        Loc.SetLanguage(language);
    }

    private void TapToClickCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        bool enabled = TapToClickCheckBox.IsChecked == true;
        try
        {
            _host.SetTapToClick(enabled);
            ClickStatusText.Text = Loc.T(enabled ? "Tap.On" : "Tap.Off");
        }
        catch (Exception ex)
        {
            AppLog.Error("Tap-to-click toggle failed", ex);
            ClickStatusText.Text = Loc.Format("Tap.Failed", ex.Message);
            _loading = true;
            TapToClickCheckBox.IsChecked = _host.IsTapToClickEnabled();
            _loading = false;
        }
    }

    private void SecondaryClickCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        try
        {
            bool enabled = SecondaryClickCheckBox.IsChecked == true;
            _host.SetSecondaryClickMode(enabled ? 1 : 0);
            ClickStatusText.Text = Loc.T(enabled ? "Secondary.On" : "Secondary.Off");
        }
        catch (Exception ex)
        {
            AppLog.Error("Secondary click apply failed", ex);
            ClickStatusText.Text = Loc.Format("Secondary.Failed", ex.Message);
            bool wasLoading = _loading;
            _loading = true;
            SecondaryClickCheckBox.IsChecked = _host.GetSecondaryClickMode() == 1;
            _loading = wasLoading;
        }
    }

    private void NaturalScrollCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        bool natural = NaturalScrollCheckBox.IsChecked == true;
        try
        {
            _host.SetNaturalScrolling(natural);
            ScrollStatusText.Text = Loc.T(natural ? "Scroll.NaturalOn" : "Scroll.NaturalOff");
        }
        catch (Exception ex)
        {
            AppLog.Error("Natural scrolling toggle failed", ex);
            ScrollStatusText.Text = Loc.Format("Scroll.NaturalFailed", ex.Message);
        }
    }

    private void ZoomCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        bool enabled = ZoomCheckBox.IsChecked == true;
        try
        {
            _host.SetZoomEnabled(enabled);
            ScrollStatusText.Text = Loc.T(enabled ? "Scroll.ZoomOn" : "Scroll.ZoomOff");
        }
        catch (Exception ex)
        {
            AppLog.Error("Zoom toggle failed", ex);
            ScrollStatusText.Text = Loc.Format("Scroll.ZoomFailed", ex.Message);
        }
    }

    private void PanSensitivitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PanSensitivityValueText is not null)
            PanSensitivityValueText.Text = ((int)Math.Round(e.NewValue)).ToString();

        if (_loading) return;
        RestartTimer(_scrollApplyTimer);
    }

    private void ApplyScrollFromSlider()
    {
        if (_loading) return;

        try
        {
            _host.ApplyScrollSensitivity((int)Math.Round(PanSensitivitySlider.Value));
            AppLog.Info($"Scroll auto-applied: {(int)Math.Round(PanSensitivitySlider.Value)}");
            ScrollStatusText.Text = Loc.T("Scroll.Applied");
        }
        catch (Exception ex)
        {
            AppLog.Error("Scroll settings auto-apply failed", ex);
            ScrollStatusText.Text = Loc.Format("Scroll.SaveFailed", ex.Message);
        }
    }

    private void RestoreScroll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _scrollApplyTimer.Stop();
            _host.RestoreScrollSensitivity();
            int restored = _host.ReadScrollSensitivity();

            bool wasLoading = _loading;
            _loading = true;
            PanSensitivitySlider.Value = restored;
            _loading = wasLoading;

            ScrollStatusText.Text = Loc.Format("Scroll.Restored", restored);
        }
        catch (Exception ex)
        {
            AppLog.Error("Scroll settings restore failed", ex);
            ScrollStatusText.Text = Loc.Format("Scroll.RestoreFailed", ex.Message);
        }
    }

    private void PointerControl_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        RestartTimer(_pointerApplyTimer);
    }

    private void ApplyPointerFromControls()
    {
        if (_loading) return;

        if (!uint.TryParse(StopPressureBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out uint stopPressure) ||
            !uint.TryParse(StopSizeBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out uint stopSize))
        {
            PointerStatusText.Text = Loc.T("Pointer.Invalid");
            return;
        }

        try
        {
            DriverSettings current = DriverSettingsStore.Read();
            DriverSettings updated = current with
            {
                StopPressure = stopPressure,
                StopSize = stopSize,
                IgnoreNearFingers = IgnoreNearFingersCheck.IsChecked == true,
                PalmRejection = PalmRejectionCheck.IsChecked == true,
                IgnoreButtonFinger = IgnoreButtonFingerCheck.IsChecked == true,
            };
            PointerStatusText.Text = Loc.T("Pointer.Saving");
            _host.ApplyPointer(updated);
            AppLog.Info("Pointer settings auto-applied");
            PointerStatusText.Text = Loc.T("Pointer.Applied");
        }
        catch (Exception ex)
        {
            AppLog.Error("Pointer settings auto-apply failed", ex);
            PointerStatusText.Text = Loc.Format("Pointer.SaveFailed", ex.Message);
            LoadDriverSettings();
        }
    }

    private void ReloadPointer_Click(object sender, RoutedEventArgs e)
    {
        _pointerApplyTimer.Stop();
        LoadDriverSettings();
        PointerStatusText.Text = Loc.T("Pointer.Loaded");
    }

    private void ApplyLiftPreset_Click(object sender, RoutedEventArgs e)
    {
        _pointerApplyTimer.Stop();

        bool wasLoading = _loading;
        _loading = true;
        StopPressureBox.Text = "0";
        StopSizeBox.Text = "7";
        _loading = wasLoading;

        ApplyPointerFromControls();
    }

    private void ResetPointClick_Click(object sender, RoutedEventArgs e)
    {
        _clickApplyTimer.Stop();

        try
        {
            bool wasLoading = _loading;
            _loading = true;
            CursorSpeedSlider.Value = TouchpadSettingsStore.DefaultCursorSpeed;
            ClickStrengthSlider.Value = IndexFromStrength(HapticSettings.Default.ClickStrength);
            QuietClickCheckBox.IsChecked = false;
            MaximumFeedbackCheckBox.IsChecked = false;
            DisableFeedbackCheckBox.IsChecked = false;
            TapToClickCheckBox.IsChecked = false;
            SecondaryClickCheckBox.IsChecked = false;
            _loading = wasLoading;

            _host.SetCursorSpeed(TouchpadSettingsStore.DefaultCursorSpeed);
            _host.SetTapToClick(false);
            _host.SetSecondaryClickMode(0);
            _host.ApplyHaptics(HapticSettings.Default);
            LoadDriverSettings();

            ClickStatusText.Text = Loc.T("Reset.PointDone");
            AppLog.Info("Point & Click reset to defaults");
        }
        catch (Exception ex)
        {
            AppLog.Error("Point & Click reset failed", ex);
            ClickStatusText.Text = Loc.Format("Reset.PointFailed", ex.Message);
            LoadDriverSettings();
        }
    }

    private void ResetScrollZoom_Click(object sender, RoutedEventArgs e)
    {
        _scrollApplyTimer.Stop();

        try
        {
            bool wasLoading = _loading;
            _loading = true;
            NaturalScrollCheckBox.IsChecked = true;
            ZoomCheckBox.IsChecked = true;
            PanSensitivitySlider.Value = 50;
            _loading = wasLoading;

            _host.SetNaturalScrolling(true);
            _host.SetZoomEnabled(true);
            _host.ApplyScrollSensitivity(50);

            ScrollStatusText.Text = Loc.T("Reset.ScrollDone");
            AppLog.Info("Scroll & Zoom reset to defaults");
        }
        catch (Exception ex)
        {
            AppLog.Error("Scroll & Zoom reset failed", ex);
            ScrollStatusText.Text = Loc.Format("Reset.ScrollFailed", ex.Message);
        }
    }

    private void ResetMoreGestures_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool wasLoading = _loading;
            _loading = true;
            DragCheckBox.IsChecked = false;
            DragGainSlider.Value = 0.5;
            _loading = wasLoading;

            if (_host.Drag.IsEnabled)
                _host.SetDragEnabled(false);
            _host.SetDragGain(0.5);

            DragStatusText.Text = Loc.T("Reset.DragDone");
            AppLog.Info("More Gestures reset to defaults");
        }
        catch (Exception ex)
        {
            AppLog.Error("More Gestures reset failed", ex);
            DragStatusText.Text = Loc.Format("Reset.DragFailed", ex.Message);
            SetDragChecked(_host.Drag.IsEnabled);
        }
    }

    private void ResetAdvanced_Click(object sender, RoutedEventArgs e)
    {
        _pointerApplyTimer.Stop();

        try
        {
            bool wasLoading = _loading;
            _loading = true;
            StopPressureBox.Text = DriverSettings.Default.StopPressure
                .ToString(CultureInfo.InvariantCulture);
            StopSizeBox.Text = DriverSettings.Default.StopSize
                .ToString(CultureInfo.InvariantCulture);
            IgnoreNearFingersCheck.IsChecked = DriverSettings.Default.IgnoreNearFingers;
            PalmRejectionCheck.IsChecked = DriverSettings.Default.PalmRejection;
            IgnoreButtonFingerCheck.IsChecked = DriverSettings.Default.IgnoreButtonFinger;
            StartWithWindowsCheckBox.IsChecked = true;
            _loading = wasLoading;

            DriverSettings current = DriverSettingsStore.Read();
            _host.ApplyPointer(current with
            {
                StopPressure = DriverSettings.Default.StopPressure,
                StopSize = DriverSettings.Default.StopSize,
                IgnoreNearFingers = DriverSettings.Default.IgnoreNearFingers,
                IgnoreButtonFinger = DriverSettings.Default.IgnoreButtonFinger,
                PalmRejection = DriverSettings.Default.PalmRejection,
            });

            StartupManager.SetEnabled(true);
            LoadDriverSettings();

            PointerStatusText.Text = Loc.T("Reset.AdvancedDone");
            AppLog.Info("Advanced reset to defaults");
        }
        catch (Exception ex)
        {
            AppLog.Error("Advanced reset failed", ex);
            PointerStatusText.Text = Loc.Format("Reset.AdvancedFailed", ex.Message);
            LoadDriverSettings();
        }
    }

    private static void RestartTimer(DispatcherTimer timer)
    {
        timer.Stop();
        timer.Start();
    }

    private void DragCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        try
        {
            _host.SetDragEnabled(DragCheckBox.IsChecked == true);
            DragStatusText.Text = Loc.T(_host.Drag.IsEnabled ? "Drag.On" : "Drag.Off");
        }
        catch (Exception ex)
        {
            AppLog.Error("Drag toggle failed", ex);
            DragStatusText.Text = Loc.Format("Drag.ToggleFailed", ex.Message);
            SetDragChecked(_host.Drag.IsEnabled);
        }
    }

    private void DragGainSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DragGainValueText is not null)
            DragGainValueText.Text = e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);

        if (_loading) return;

        try
        {
            _host.SetDragGain(e.NewValue);
        }
        catch (Exception ex)
        {
            AppLog.Error("Drag gain apply failed", ex);
            DragStatusText.Text = Loc.Format("Drag.SpeedFailed", ex.Message);
        }
    }

    private void StartWithWindowsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        try
        {
            StartupManager.SetEnabled(StartWithWindowsCheckBox.IsChecked == true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Loc.Format("Dialog.StartWithWindowsFailed", ex.Message),
                "OpenTrackpad",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _loading = true;
            StartWithWindowsCheckBox.IsChecked = StartupManager.IsEnabled();
            _loading = false;
        }
    }

    private void OpenWindowsTouchpad_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings("ms-settings:devices-touchpad", "Open Windows touchpad settings failed");
    }

    private void OpenBluetoothSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings("ms-settings:bluetooth", "Open Bluetooth settings failed");
    }

    private void OpenSettings(string uri, string logMessage)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLog.Error(logMessage, ex);
        }
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Root)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("Open log folder failed", ex);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void RestartAdmin_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult result = MessageBox.Show(
            Loc.T("Dialog.RestartAdminConfirm"),
            "OpenTrackpad",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        string? exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe)) return;

        string arguments = string.Empty;
        if (Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            string? dll = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrWhiteSpace(dll))
                arguments = $"\"{dll}\"";
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
            });
            CloseForExit();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled the UAC prompt.
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Loc.Format("Dialog.RestartAdminFailed", ex.Message),
                "OpenTrackpad",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
        HiddenToTray?.Invoke();
    }
}
