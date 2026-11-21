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
using OpenTrackpad.Core.Health;
using OpenTrackpad.Interop.Driver;

namespace OpenTrackpad.App;

public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private readonly DispatcherTimer _scrollApplyTimer;
    private readonly DispatcherTimer _pointerApplyTimer;
    private BatteryReading? _lastBatteryReading;
    private string? _lastBatteryError;
    private bool _deviceDisconnected;
    private bool _allowClose;
    private bool _loading = true;
    private string? _lastHealthSignature;
    private HealthIssue? _healthFixIssue;

    public MainWindow(AppHost host)
    {
        InitializeComponent();
        _host = host;
        Title = $"OpenTrackpad v{AppVersion.Display}";
        VersionText.Text = $"v{AppVersion.Display}";
        FooterVersionText.Text = $"OpenTrackpad v{AppVersion.Display}";

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
        SensitivityComboBox.SelectedIndex = host.GetTouchpadSensitivity();
        TapAndDragCheckBox.IsChecked = host.IsTapAndDragEnabled();
        RightClickZoneCheckBox.IsChecked = host.IsRightClickZoneEnabled();
        LeaveOnWithMouseCheckBox.IsChecked = host.IsLeaveOnWithMouse();
        UpdateTapAndDragEnabled();

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
        _deviceDisconnected = false;
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
        _deviceDisconnected = false;
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

    public void SetDeviceDisconnected()
    {
        _deviceDisconnected = true;
        _lastBatteryReading = null;
        _lastBatteryError = null;
        BatteryText.Text = Loc.T("Status.BatteryUnavailable");
        BatteryText.ToolTip = null;
        StatusText.Text = Loc.T("Status.NoDeviceBattery");
    }

    public void RefreshHealth()
    {
        HealthInputs inputs = _host.GetHealthInputs();
        string signature =
            $"driver={inputs.DriverInstalled},usb={inputs.UsbDevicePresent}," +
            $"bt={inputs.BluetoothDevicePresent},usbAccess={inputs.UsbAccessAllowed}," +
            $"btAccess={inputs.BluetoothAccessAllowed}";
        if (!string.Equals(signature, _lastHealthSignature, StringComparison.Ordinal))
        {
            _lastHealthSignature = signature;
            AppLog.Info("Health: " + signature);
        }

        IReadOnlyList<HealthFinding> findings = HealthEvaluator.Evaluate(inputs);
        HealthFinding? primary = findings
            .OrderByDescending(finding => finding.Severity)
            .FirstOrDefault();

        if (primary is null)
        {
            _healthFixIssue = null;
            HealthBanner.Visibility = Visibility.Collapsed;
        }
        else
        {
            _healthFixIssue = primary.Issue;
            (string titleKey, string detailKey, string fixKey) = primary.Issue switch
            {
                HealthIssue.DriverMissing => (
                    "Health.DriverMissing.Title",
                    "Health.DriverMissing.Detail",
                    "Health.Fix.Troubleshooting"),
                HealthIssue.DeviceMissing => (
                    "Health.DeviceMissing.Title",
                    "Health.DeviceMissing.Detail",
                    "Health.Fix.Bluetooth"),
                _ => (
                    "Health.AccessDenied.Title",
                    "Health.AccessDenied.Detail",
                    "Health.Fix.EnableAccess"),
            };
            HealthTitleText.Text = Loc.T(titleKey);
            HealthDetailText.Text = Loc.T(detailKey);
            HealthFixButton.Content = Loc.T(fixKey);
            HealthBanner.Visibility = Visibility.Visible;
        }

        HealthDriverValueText.Text = Loc.T(
            inputs.DriverInstalled ? "Health.Value.Installed" : "Health.Value.Missing");
        HealthDeviceValueText.Text = inputs.UsbDevicePresent && inputs.BluetoothDevicePresent
            ? Loc.T("Health.Value.Both")
            : inputs.UsbDevicePresent
                ? Loc.T("Health.Value.Usb")
                : inputs.BluetoothDevicePresent
                    ? Loc.T("Health.Value.Bluetooth")
                    : Loc.T("Health.Value.NotConnected");

        bool anyDevice = inputs.UsbDevicePresent || inputs.BluetoothDevicePresent;
        bool accessDenied =
            (inputs.UsbDevicePresent && !inputs.UsbAccessAllowed) ||
            (inputs.BluetoothDevicePresent && !inputs.BluetoothAccessAllowed);
        HealthAccessValueText.Text = anyDevice
            ? Loc.T(accessDenied ? "Health.Value.NeedsAccess" : "Health.Value.Ok")
            : Loc.T("Health.Value.NotApplicable");
        HealthBatteryValueText.Text = _lastBatteryReading is { } reading
            ? $"{reading.Level}%"
            : Loc.T("Health.Value.Unavailable");
        EnableAccessButton.Visibility = _host.IsAdministrator
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void RefreshHealth_Click(object sender, RoutedEventArgs e)
    {
        _host.DeviceMonitor.Poll();
        _host.RefreshBattery();
        RefreshHealth();
        HealthStatusText.Text = Loc.T("Health.Refreshed");
    }

    private async void ShowDriverStatus_Click(object sender, RoutedEventArgs e)
    {
        HealthStatusText.Text = Loc.T("Health.Running");
        try
        {
            string output = await Task.Run(() => DriverStatusRunner.RunStatus());
            HealthStatusText.Text = string.Empty;
            var window = new DriverStatusWindow(
                string.IsNullOrWhiteSpace(output) ? Loc.T("Health.NoOutput") : output)
            {
                Owner = this,
            };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            AppLog.Error("Driver status failed", ex);
            HealthStatusText.Text = Loc.Format("Health.StatusFailed", ex.Message);
        }
    }

    private async void EnableAccess_Click(object sender, RoutedEventArgs e) =>
        await EnableAccessAsync();

    private async Task EnableAccessAsync()
    {
        MessageBoxResult result = MessageBox.Show(
            Loc.T("Health.EnableAccessConfirm"),
            "OpenTrackpad",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        HealthStatusText.Text = Loc.T("Health.EnableAccessRunning");
        EnableAccessButton.IsEnabled = false;
        try
        {
            int exitCode = await Task.Run(DriverStatusRunner.RunElevatedAccessFix);
            if (exitCode != 0)
            {
                HealthStatusText.Text = Loc.Format("Health.EnableAccessFailed", exitCode);
                return;
            }

            HealthStatusText.Text = Loc.T("Health.EnableAccessDone");
            _host.RefreshBattery();
            _host.DeviceMonitor.Poll();
            RefreshHealth();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            HealthStatusText.Text = Loc.T("Health.EnableAccessCancelled");
        }
        catch (Exception ex)
        {
            AppLog.Error("Enable user access failed", ex);
            HealthStatusText.Text = Loc.Format("Health.EnableAccessError", ex.Message);
        }
        finally
        {
            EnableAccessButton.IsEnabled = true;
        }
    }

    private void HealthFix_Click(object sender, RoutedEventArgs e)
    {
        switch (_healthFixIssue)
        {
            case HealthIssue.AccessDeniedUsb:
            case HealthIssue.AccessDeniedBluetooth:
                _ = EnableAccessAsync();
                break;
            case HealthIssue.DeviceMissing:
                OpenBluetoothSettings_Click(sender, e);
                break;
            case HealthIssue.DriverMissing:
                OpenTroubleshooting();
                break;
        }
    }

    private void OpenTroubleshooting()
    {
        try
        {
            string target = ScriptLocator.Find("docs/troubleshooting.md")
                ?? "https://github.com/Jutipong/open-trackpad/blob/main/docs/troubleshooting.md";
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error("Open troubleshooting failed", ex);
        }
    }

    public void RefreshLocalizedText()
    {
        AdminText.Text = Loc.T(_host.IsAdministrator ? "Header.Admin" : "Header.User");
        DragStatusText.Text = Loc.T(_host.Drag.IsEnabled ? "Drag.On" : "Drag.Off");
        ScrollStatusText.Text = string.Empty;
        PointerStatusText.Text = string.Empty;
        if (_deviceDisconnected)
            SetDeviceDisconnected();
        else if (_lastBatteryError is not null)
            SetBatteryError(_lastBatteryError);
        else if (_lastBatteryReading is { } reading)
            SetBattery(reading);
        LoadDriverSettings();
        RefreshHealth();
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
        uint selectedStrength = mode switch
        {
            HapticMode.Quiet => 0,
            HapticMode.Preset => settings.FeedbackClick,
            _ => backup?.ClickStrength ?? HapticSettings.FirmStrength,
        };

        int selectedIndex = IndexFromStrength(selectedStrength);
        ClickStrengthComboBox.SelectedIndex = selectedIndex;
        ClickStrengthComboBox.IsEnabled = mode != HapticMode.Disabled;
        DisableFeedbackCheckBox.IsChecked = mode == HapticMode.Disabled;
        PreviewClickButton.IsEnabled = selectedIndex > 0 && mode != HapticMode.Disabled;
        ClickStatusText.Text = mode switch
        {
            HapticMode.Quiet => Loc.T("Click.OffOn"),
            HapticMode.Maximum => Loc.T("Click.MaxOn"),
            HapticMode.Disabled => Loc.T("Click.DisabledOn"),
            _ => string.Empty,
        };

        _loading = wasLoading;
    }

    private static int IndexFromStrength(uint strength) => strength switch
    {
        0 => 0,
        HapticSettings.LightStrength => 1,
        HapticSettings.MediumStrength => 2,
        HapticSettings.FirmStrength => 3,
        _ => 2,
    };

    private static HapticPreset PresetFromIndex(int index) => index switch
    {
        0 => HapticPreset.Off,
        1 => HapticPreset.Light,
        2 => HapticPreset.Medium,
        _ => HapticPreset.Firm,
    };

    private static string PresetNameFromIndex(int index) => Loc.T(index switch
    {
        0 => "Click.Off",
        1 => "Click.Light",
        2 => "Click.Medium",
        _ => "Click.Firm",
    });

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _deviceDisconnected = false;
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

    private void ClickStrengthComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ClickStrengthComboBox.SelectedIndex < 0) return;

        PreviewClickButton.IsEnabled = ClickStrengthComboBox.SelectedIndex > 0;
        ApplyClickFromSelection();
    }

    private void PreviewClick_Click(object sender, RoutedEventArgs e)
    {
        int index = ClickStrengthComboBox.SelectedIndex;
        if (index <= 0) return;

        HapticPreset preset = PresetFromIndex(index);
        try
        {
            uint strength = HapticSettings.FromPreset(preset).ClickStrength;
            ControlPanelResponse response = _host.PreviewHaptic(strength);
            ClickStatusText.Text = response.Ok
                ? Loc.Format("Click.PreviewDone", PresetNameFromIndex(index))
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

    private void ApplyClickFromSelection()
    {
        if (_loading || ClickStrengthComboBox.SelectedIndex < 0) return;

        HapticPreset preset = PresetFromIndex(ClickStrengthComboBox.SelectedIndex);
        HapticSettings haptics = HapticSettings.FromPreset(preset);

        DriverSettings current = DriverSettingsStore.Read();
        if (current.FeedbackClick == haptics.ClickStrength &&
            current.FeedbackRelease == haptics.ReleaseStrength &&
            !current.ButtonDisabled)
        {
            ClickStatusText.Text = Loc.Format(
                "Click.Already", PresetNameFromIndex(ClickStrengthComboBox.SelectedIndex));
            return;
        }

        ClickStatusText.Text = Loc.T("Click.Saving");
        try
        {
            _host.ApplyHaptics(haptics);
            LoadDriverSettings();
            AppLog.Info($"Click auto-applied: {preset}");
            ClickStatusText.Text = Loc.Format(
                "Click.Applied", PresetNameFromIndex(ClickStrengthComboBox.SelectedIndex));
        }
        catch (Exception ex)
        {
            AppLog.Error("Haptic auto-apply failed", ex);
            ClickStatusText.Text = Loc.Format("Click.SaveFailed", ex.Message);
            LoadDriverSettings();
        }
    }

    private void DisableFeedbackCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        HapticMode mode = DisableFeedbackCheckBox.IsChecked == true
            ? HapticMode.Disabled
            : HapticMode.Preset;

        try
        {
            ClickStatusText.Text = Loc.T("Click.Saving");
            _host.SetHapticMode(mode);
            LoadDriverSettings();
            ClickStatusText.Text = _host.GetHapticMode() switch
            {
                HapticMode.Disabled => Loc.T("Click.DisabledOn"),
                HapticMode.Quiet => Loc.T("Click.OffOn"),
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

        UpdateTapAndDragEnabled();
    }

    private void UpdateTapAndDragEnabled()
    {
        TapAndDragCheckBox.IsEnabled = TapToClickCheckBox.IsChecked == true;
    }

    private void TapAndDragCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        bool enabled = TapAndDragCheckBox.IsChecked == true;
        try
        {
            _host.SetTapAndDrag(enabled);
            ClickStatusText.Text = Loc.T(enabled ? "TapDrag.On" : "TapDrag.Off");
        }
        catch (Exception ex)
        {
            AppLog.Error("Tap-and-drag toggle failed", ex);
            ClickStatusText.Text = Loc.Format("TapDrag.Failed", ex.Message);
            _loading = true;
            TapAndDragCheckBox.IsChecked = _host.IsTapAndDragEnabled();
            _loading = false;
        }
    }

    private void SensitivityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SensitivityComboBox.SelectedIndex < 0) return;

        try
        {
            _host.SetTouchpadSensitivity(SensitivityComboBox.SelectedIndex);
            ClickStatusText.Text = Loc.T("Sensitivity.Applied");
        }
        catch (Exception ex)
        {
            AppLog.Error("Touchpad sensitivity apply failed", ex);
            ClickStatusText.Text = Loc.Format("Sensitivity.Failed", ex.Message);
            _loading = true;
            SensitivityComboBox.SelectedIndex = _host.GetTouchpadSensitivity();
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

    private void RightClickZoneCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        try
        {
            bool enabled = RightClickZoneCheckBox.IsChecked == true;
            _host.SetRightClickZone(enabled);
            ClickStatusText.Text = Loc.T(enabled ? "RightClickZone.On" : "RightClickZone.Off");
        }
        catch (Exception ex)
        {
            AppLog.Error("Right-click zone apply failed", ex);
            ClickStatusText.Text = Loc.Format("RightClickZone.Failed", ex.Message);
            bool wasLoading = _loading;
            _loading = true;
            RightClickZoneCheckBox.IsChecked = _host.IsRightClickZoneEnabled();
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

    private void LeaveOnWithMouseCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        bool enabled = LeaveOnWithMouseCheckBox.IsChecked == true;
        try
        {
            _host.SetLeaveOnWithMouse(enabled);
            PointerStatusText.Text = Loc.T(enabled ? "LeaveOnWithMouse.On" : "LeaveOnWithMouse.Off");
        }
        catch (Exception ex)
        {
            AppLog.Error("Leave-on-with-mouse toggle failed", ex);
            PointerStatusText.Text = Loc.Format("LeaveOnWithMouse.Failed", ex.Message);
            _loading = true;
            LeaveOnWithMouseCheckBox.IsChecked = _host.IsLeaveOnWithMouse();
            _loading = false;
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
        try
        {
            bool wasLoading = _loading;
            _loading = true;
            CursorSpeedSlider.Value = TouchpadSettingsStore.DefaultCursorSpeed;
            SensitivityComboBox.SelectedIndex = TouchpadSettingsStore.DefaultSensitivity;
            ClickStrengthComboBox.SelectedIndex =
                IndexFromStrength(HapticSettings.Default.ClickStrength);
            DisableFeedbackCheckBox.IsChecked = false;
            TapToClickCheckBox.IsChecked = false;
            TapAndDragCheckBox.IsChecked = true;
            SecondaryClickCheckBox.IsChecked = false;
            RightClickZoneCheckBox.IsChecked = false;
            _loading = wasLoading;
            UpdateTapAndDragEnabled();

            _host.SetCursorSpeed(TouchpadSettingsStore.DefaultCursorSpeed);
            _host.SetTouchpadSensitivity(TouchpadSettingsStore.DefaultSensitivity);
            _host.SetTapToClick(false);
            _host.SetTapAndDrag(true);
            _host.SetSecondaryClickMode(0);
            _host.SetRightClickZone(false);
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
            LeaveOnWithMouseCheckBox.IsChecked = true;
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

            _host.SetLeaveOnWithMouse(true);
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

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        ExportDiagnosticsButton.IsEnabled = false;
        DiagnosticsStatusText.Text = Loc.T("Advanced.Exporting");

        try
        {
            string path = await Task.Run(() => DiagnosticsExporter.Export(_host));
            DiagnosticsStatusText.Text = Loc.Format("Advanced.ExportDone", path);
            AppLog.Info("Diagnostics exported: " + path);

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("Open diagnostics folder failed", ex);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Diagnostics export failed", ex);
            DiagnosticsStatusText.Text = Loc.Format("Advanced.ExportFailed", ex.Message);
        }
        finally
        {
            ExportDiagnosticsButton.IsEnabled = true;
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
