using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
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

        AdminText.Text = host.IsAdministrator
            ? "สิทธิ์: administrator — แก้ไขค่าของไดรเวอร์ได้"
            : "สิทธิ์: ผู้ใช้ทั่วไป — การบันทึกค่าของไดรเวอร์จะขอ UAC";
        RestartAdminButton.Visibility = host.IsAdministrator ? Visibility.Collapsed : Visibility.Visible;

        LoadDriverSettings();
        CursorSpeedSlider.Value = host.GetCursorSpeed();
        CursorSpeedValueText.Text = ((int)CursorSpeedSlider.Value).ToString();
        TapToClickCheckBox.IsChecked = host.IsTapToClickEnabled();
        SecondaryClickCombo.SelectedIndex = host.GetSecondaryClickMode();
        NaturalScrollCheckBox.IsChecked = host.IsNaturalScrolling();
        ZoomCheckBox.IsChecked = host.IsZoomEnabled();
        PanSensitivitySlider.Value = host.ReadScrollSensitivity();
        PanSensitivityValueText.Text = ((int)Math.Round(PanSensitivitySlider.Value)).ToString();
        StartWithWindowsCheckBox.IsChecked = StartupManager.IsEnabled();
        DragCheckBox.IsChecked = host.Drag.IsEnabled;
        DragStatusText.Text = host.Drag.IsEnabled ? "เปิดอยู่" : "ปิดอยู่";

        _loading = false;
    }

    public event Action<bool>? DragEnabledChanged;

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

    public void SetBattery(int level)
    {
        BatteryText.Text = $"{level}%";
        BatteryText.ToolTip = null;
        StatusText.Text = "เชื่อมต่อผ่าน USB";
    }

    public void SetBatteryError(string error)
    {
        BatteryText.Text = "ไม่พร้อมใช้งาน";
        BatteryText.ToolTip = error;
        StatusText.Text = "อ่านแบตเตอรี่ไม่ได้ (ตรวจสอบสาย USB / ไดรเวอร์)";
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
        ButtonDisabledCheck.IsChecked = settings.ButtonDisabled;

        HapticSettings? quietBackup = _host.Settings.QuietClickBackup;
        uint sliderStrength = settings.FeedbackClick == 0
            ? quietBackup?.ClickStrength ?? HapticSettings.FirmStrength
            : settings.FeedbackClick;

        ClickStrengthSlider.Value = IndexFromStrength(sliderStrength);
        ClickStrengthValueText.Text = PresetNameFromIndex((int)ClickStrengthSlider.Value);
        QuietClickCheckBox.IsChecked = settings.FeedbackClick == 0 && settings.FeedbackRelease == 0;
        ClickStatusText.Text = settings.FeedbackClick == 0 && settings.FeedbackRelease == 0
            ? "Quiet Click เปิดอยู่"
            : string.Empty;

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
        StatusText.Text = "กำลังอ่านแบตเตอรี่...";
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
            StatusText.Text = $"ตั้งค่า tracking speed ไม่สำเร็จ: {ex.Message}";
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
            ControlPanelResponse response = _host.Pipe.Actuate(strength);
            ClickStatusText.Text = response.Ok
                ? $"ทดลองสั่นแล้ว ({PresetNameFromIndex((int)ClickStrengthSlider.Value)})"
                : $"ไดรเวอร์ตอบสถานะ 0x{response.Status:X2}";
        }
        catch (Exception ex)
        {
            AppLog.Error("Haptic preview failed", ex);
            ClickStatusText.Text = $"ทดลองสั่นไม่สำเร็จ: {ex.Message}";
        }
    }

    private void ApplyClickFromSlider()
    {
        if (_loading) return;

        HapticPreset preset = PresetFromIndex((int)Math.Round(ClickStrengthSlider.Value));
        ClickStatusText.Text = "กำลังบันทึก (รอ UAC)...";
        try
        {
            _host.ApplyHaptics(HapticSettings.FromPreset(preset));
            LoadDriverSettings();
            AppLog.Info($"Click auto-applied: {preset}");
            ClickStatusText.Text = $"บันทึก Click = {PresetNameFromIndex((int)ClickStrengthSlider.Value)} แล้ว";
        }
        catch (Exception ex)
        {
            AppLog.Error("Haptic auto-apply failed", ex);
            ClickStatusText.Text = $"บันทึกไม่สำเร็จ: {ex.Message}";
            LoadDriverSettings();
        }
    }

    private void QuietClickCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        _clickApplyTimer.Stop();
        bool enabled = QuietClickCheckBox.IsChecked == true;
        try
        {
            _host.SetQuietClick(enabled);
            LoadDriverSettings();
            ClickStatusText.Text = enabled
                ? "Quiet Click เปิดอยู่ (ไม่มีการสั่น)"
                : "Quiet Click ปิดแล้ว";
        }
        catch (Exception ex)
        {
            AppLog.Error("Quiet Click toggle failed", ex);
            ClickStatusText.Text = $"สลับ Quiet Click ไม่สำเร็จ: {ex.Message}";
            LoadDriverSettings();
        }
    }

    private void TapToClickCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        bool enabled = TapToClickCheckBox.IsChecked == true;
        try
        {
            _host.SetTapToClick(enabled);
            ClickStatusText.Text = enabled ? "เปิด tap-to-click แล้ว" : "ปิด tap-to-click แล้ว";
        }
        catch (Exception ex)
        {
            AppLog.Error("Tap-to-click toggle failed", ex);
            ClickStatusText.Text = $"สลับ tap-to-click ไม่สำเร็จ: {ex.Message}";
            _loading = true;
            TapToClickCheckBox.IsChecked = _host.IsTapToClickEnabled();
            _loading = false;
        }
    }

    private void SecondaryClickCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading) return;

        try
        {
            _host.SetSecondaryClickMode(SecondaryClickCombo.SelectedIndex);
            ClickStatusText.Text = SecondaryClickCombo.SelectedIndex == 1
                ? "Secondary click: แตะ 2 นิ้ว"
                : "Secondary click: ปิด";
        }
        catch (Exception ex)
        {
            AppLog.Error("Secondary click apply failed", ex);
            ClickStatusText.Text = $"ตั้งค่า secondary click ไม่สำเร็จ: {ex.Message}";
        }
    }

    private void NaturalScrollCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        bool natural = NaturalScrollCheckBox.IsChecked == true;
        try
        {
            _host.SetNaturalScrolling(natural);
            ScrollStatusText.Text = natural ? "Natural scrolling เปิดอยู่" : "Natural scrolling ปิดอยู่";
        }
        catch (Exception ex)
        {
            AppLog.Error("Natural scrolling toggle failed", ex);
            ScrollStatusText.Text = $"ตั้งค่า natural scrolling ไม่สำเร็จ: {ex.Message}";
        }
    }

    private void ZoomCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        bool enabled = ZoomCheckBox.IsChecked == true;
        try
        {
            _host.SetZoomEnabled(enabled);
            ScrollStatusText.Text = enabled ? "Zoom เปิดอยู่" : "Zoom ปิดอยู่";
        }
        catch (Exception ex)
        {
            AppLog.Error("Zoom toggle failed", ex);
            ScrollStatusText.Text = $"ตั้งค่า zoom ไม่สำเร็จ: {ex.Message}";
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
            ScrollStatusText.Text = "บันทึกความเร็วการเลื่อนอัตโนมัติแล้ว";
        }
        catch (Exception ex)
        {
            AppLog.Error("Scroll settings auto-apply failed", ex);
            ScrollStatusText.Text = $"บันทึกไม่สำเร็จ: {ex.Message}";
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

            ScrollStatusText.Text = $"คืนค่าเดิมแล้ว ({restored})";
        }
        catch (Exception ex)
        {
            AppLog.Error("Scroll settings restore failed", ex);
            ScrollStatusText.Text = $"คืนค่าไม่สำเร็จ: {ex.Message}";
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
            PointerStatusText.Text = "Stop pressure / Stop size ต้องเป็นตัวเลข 0..4294967295";
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
                ButtonDisabled = ButtonDisabledCheck.IsChecked == true,
            };
            PointerStatusText.Text = "กำลังบันทึก (รอ UAC)...";
            _host.ApplyPointer(updated);
            AppLog.Info("Pointer settings auto-applied");
            PointerStatusText.Text = "บันทึกอัตโนมัติและ reload ไดรเวอร์แล้ว";
        }
        catch (Exception ex)
        {
            AppLog.Error("Pointer settings auto-apply failed", ex);
            PointerStatusText.Text = $"บันทึกไม่สำเร็จ: {ex.Message}";
            LoadDriverSettings();
        }
    }

    private void ReloadPointer_Click(object sender, RoutedEventArgs e)
    {
        _pointerApplyTimer.Stop();
        LoadDriverSettings();
        PointerStatusText.Text = "โหลดค่าจากไดรเวอร์แล้ว";
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
            DragStatusText.Text = _host.Drag.IsEnabled ? "เปิดอยู่" : "ปิดอยู่";
            DragEnabledChanged?.Invoke(_host.Drag.IsEnabled);
        }
        catch (Exception ex)
        {
            AppLog.Error("Drag toggle failed", ex);
            DragStatusText.Text = $"สลับโหมดไม่สำเร็จ: {ex.Message}";
            SetDragChecked(_host.Drag.IsEnabled);
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
                $"ตั้งค่าเริ่มพร้อม Windows ไม่สำเร็จ:\n{ex.Message}",
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
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:devices-touchpad")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("Open Windows touchpad settings failed", ex);
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

    private void HideButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void ExitButton_Click(object sender, RoutedEventArgs e) => ShutdownApplication();

    private void ShutdownApplication()
    {
        _allowClose = true;
        Application.Current.Shutdown();
    }

    private void RestartAdmin_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult result = MessageBox.Show(
            "โปรแกรมจะปิดและเปิดใหม่พร้อมสิทธิ์ administrator\nดำเนินการต่อหรือไม่?",
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
            ShutdownApplication();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled the UAC prompt.
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"เปิดใหม่แบบ administrator ไม่สำเร็จ:\n{ex.Message}",
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
    }
}
