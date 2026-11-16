using WinForms = System.Windows.Forms;

namespace OpenTrackpad.App;

public sealed class TrayIconHost : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _batteryItem;
    private readonly WinForms.ToolStripMenuItem _dragItem;

    public TrayIconHost()
    {
        _batteryItem = new WinForms.ToolStripMenuItem("แบตเตอรี่: --%") { Enabled = false };
        _dragItem = new WinForms.ToolStripMenuItem("ลาก 3 นิ้ว") { CheckOnClick = true };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("เปิดการตั้งค่า", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(_batteryItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_dragItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("ออก", null, (_, _) => ExitRequested?.Invoke());

        _dragItem.CheckedChanged += (_, _) => DragToggleRequested?.Invoke(_dragItem.Checked);

        _icon = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "OpenTrackpad",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public event Action? OpenRequested;
    public event Action? ExitRequested;
    public event Action<bool>? DragToggleRequested;

    public void SetBattery(int level)
    {
        _batteryItem.Text = $"แบตเตอรี่: {level}%";
        _icon.Text = $"OpenTrackpad — {level}%";
    }

    public void SetBatteryUnavailable()
    {
        _batteryItem.Text = "แบตเตอรี่: ไม่พร้อมใช้งาน";
        _icon.Text = "OpenTrackpad";
    }

    public void SetDragChecked(bool enabled)
    {
        if (_dragItem.Checked == enabled) return;
        _dragItem.Checked = enabled;
    }

    public void ShowAlert(string title, string message, bool critical)
    {
        _icon.ShowBalloonTip(
            5000,
            title,
            message,
            critical ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
