using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using OpenTrackpad.Core.Haptics;
using OpenTrackpad.Interop.Driver;

namespace OpenTrackpad.App;

public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private bool _allowClose;
    private bool _loading = true;

    public MainWindow(AppHost host)
    {
        InitializeComponent();
        _host = host;

        AdminText.Text = host.IsAdministrator
            ? "สิทธิ์: administrator — แก้ไขค่าของไดรเวอร์ได้"
            : "สิทธิ์: ผู้ใช้ทั่วไป — การบันทึกค่าของไดรเวอร์จะขอ UAC";
        RestartAdminButton.Visibility = host.IsAdministrator ? Visibility.Collapsed : Visibility.Visible;

        LoadDriverSettings();
        PanSensitivitySlider.Value = host.ReadScrollSensitivity();
        PanSensitivityValueText.Text = ((int)PanSensitivitySlider.Value).ToString();
        StartWithWindowsCheckBox.IsChecked = StartupManager.IsEnabled();
        DragCheckBox.IsChecked = host.Drag.IsEnabled;
        DragStatusText.Text = host.Drag.IsEnabled ? "เปิดอยู่" : "ปิดอยู่";

        _loading = false;
    }

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

    public event Action<bool>? DragEnabledChanged;

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
        DriverSettings settings = DriverSettingsStore.Read();
        StopPressureBox.Text = settings.StopPressure.ToString(CultureInfo.InvariantCulture);
        StopSizeBox.Text = settings.StopSize.ToString(CultureInfo.InvariantCulture);
        IgnoreNearFingersCheck.IsChecked = settings.IgnoreNearFingers;
        IgnoreButtonFingerCheck.IsChecked = settings.IgnoreButtonFinger;
        PalmRejectionCheck.IsChecked = settings.PalmRejection;
        InvertMovementCheck.IsChecked = settings.InvertMovement;
        ButtonDisabledCheck.IsChecked = settings.ButtonDisabled;

        int index = settings.FeedbackClick switch
        {
            0 => 0,
            HapticSettings.LightStrength => 1,
            HapticSettings.MediumStrength => 2,
            HapticSettings.FirmStrength => 3,
            _ => -1,
        };
        HapticPresetCombo.SelectedIndex = index;
        HapticStatusText.Text = index < 0
            ? $"ค่าปัจจุบันกำหนดเอง: 0x{settings.FeedbackClick:X6}"
            : string.Empty;
    }

    private HapticPreset? SelectedPreset()
    {
        if (HapticPresetCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
            return null;
        return item.Tag?.ToString() switch
        {
            "Off" => HapticPreset.Off,
            "Light" => HapticPreset.Light,
            "Medium" => HapticPreset.Medium,
            "Firm" => HapticPreset.Firm,
            _ => null,
        };
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "กำลังอ่านแบตเตอรี่...";
        _host.RefreshBattery();
    }

    private void PreviewHaptic_Click(object sender, RoutedEventArgs e)
    {
        HapticPreset? preset = SelectedPreset();
        if (preset is null)
        {
            HapticStatusText.Text = "กรุณาเลือกระดับการสั่นก่อน";
            return;
        }

        if (preset == HapticPreset.Off)
        {
            HapticStatusText.Text = "โหมดปิดไม่มีการสั่น";
            return;
        }

        try
        {
            uint strength = HapticSettings.FromPreset(preset.Value).ClickStrength;
            ControlPanelResponse response = _host.Pipe.Actuate(strength);
            HapticStatusText.Text = response.Ok
                ? $"ทดลองสั่นแล้ว (0x{strength:X6})"
                : $"ไดรเวอร์ตอบสถานะ 0x{response.Status:X2}";
        }
        catch (Exception ex)
        {
            AppLog.Error("Haptic preview failed", ex);
            HapticStatusText.Text = $"ทดลองสั่นไม่สำเร็จ: {ex.Message}";
        }
    }

    private void ApplyHaptic_Click(object sender, RoutedEventArgs e)
    {
        HapticPreset? preset = SelectedPreset();
        if (preset is null)
        {
            HapticStatusText.Text = "กรุณาเลือกระดับการสั่นก่อน";
            return;
        }

        try
        {
            _host.ApplyHaptics(HapticSettings.FromPreset(preset.Value));
            HapticStatusText.Text = "บันทึกและ reload ไดรเวอร์แล้ว";
        }
        catch (Exception ex)
        {
            AppLog.Error("Haptic apply failed", ex);
            HapticStatusText.Text = $"บันทึกไม่สำเร็จ: {ex.Message}";
        }
    }

    private void ApplyPointer_Click(object sender, RoutedEventArgs e)
    {
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
                IgnoreButtonFinger = IgnoreButtonFingerCheck.IsChecked == true,
                PalmRejection = PalmRejectionCheck.IsChecked == true,
                InvertMovement = InvertMovementCheck.IsChecked == true,
                ButtonDisabled = ButtonDisabledCheck.IsChecked == true,
            };
            _host.ApplyPointer(updated);
            PointerStatusText.Text = "บันทึกและ reload ไดรเวอร์แล้ว";
        }
        catch (Exception ex)
        {
            AppLog.Error("Pointer settings apply failed", ex);
            PointerStatusText.Text = $"บันทึกไม่สำเร็จ: {ex.Message}";
        }
    }

    private void ReloadPointer_Click(object sender, RoutedEventArgs e)
    {
        LoadDriverSettings();
        PointerStatusText.Text = "โหลดค่าจากไดรเวอร์แล้ว";
    }

    private void PanSensitivitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PanSensitivityValueText is not null)
            PanSensitivityValueText.Text = ((int)Math.Round(e.NewValue)).ToString();
    }

    private void ApplyScroll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _host.ApplyScrollSensitivity((int)Math.Round(PanSensitivitySlider.Value));
            ScrollStatusText.Text = "บันทึกแล้ว (หากยังไม่เปลี่ยน ให้ปิด/เปิดหน้า Touchpad Settings)";
        }
        catch (Exception ex)
        {
            AppLog.Error("Scroll settings apply failed", ex);
            ScrollStatusText.Text = $"บันทึกไม่สำเร็จ: {ex.Message}";
        }
    }

    private void RestoreScroll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _host.RestoreScrollSensitivity();
            int restored = _host.ReadScrollSensitivity();
            PanSensitivitySlider.Value = restored;
            ScrollStatusText.Text = $"คืนค่าเดิมแล้ว ({restored})";
        }
        catch (Exception ex)
        {
            ScrollStatusText.Text = $"คืนค่าไม่สำเร็จ: {ex.Message}";
        }
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
            _loading = true;
            DragCheckBox.IsChecked = _host.Drag.IsEnabled;
            _loading = false;
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
