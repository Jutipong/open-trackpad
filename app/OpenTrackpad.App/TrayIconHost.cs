using System.IO;
using WinForms = System.Windows.Forms;

namespace OpenTrackpad.App;

public sealed class TrayIconHost : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _appIcon;
    private readonly WinForms.ToolStripMenuItem _batteryItem;

    public TrayIconHost()
    {
        _batteryItem = new WinForms.ToolStripMenuItem("แบตเตอรี่: --%") { Enabled = false };

        var menu = new WinForms.ContextMenuStrip
        {
            BackColor = System.Drawing.Color.FromArgb(37, 37, 40),
            ForeColor = System.Drawing.Color.FromArgb(232, 232, 232),
            Renderer = new WinForms.ToolStripProfessionalRenderer(new DarkMenuColorTable()),
        };
        menu.Items.Add("เปิดการตั้งค่า", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(_batteryItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());
        foreach (WinForms.ToolStripItem item in menu.Items)
            item.ForeColor = System.Drawing.Color.FromArgb(232, 232, 232);

        _appIcon = LoadAppIcon();
        _icon = new WinForms.NotifyIcon
        {
            Icon = _appIcon,
            Text = "OpenTrackpad",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public event Action? OpenRequested;
    public event Action? ExitRequested;

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
        _appIcon.Dispose();
    }

    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("app.ico", UriKind.Relative));
            if (resource is not null)
            {
                using Stream stream = resource.Stream;
                return new System.Drawing.Icon(stream);
            }
        }
        catch
        {
            // Fall through to the executable icon.
        }

        try
        {
            string? path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is not null) return icon;
            }
        }
        catch
        {
            // Fall through to the generic icon.
        }

        return System.Drawing.SystemIcons.Application;
    }

    private sealed class DarkMenuColorTable : WinForms.ProfessionalColorTable
    {
        private static readonly System.Drawing.Color Surface = System.Drawing.Color.FromArgb(37, 37, 40);
        private static readonly System.Drawing.Color Hover = System.Drawing.Color.FromArgb(58, 58, 64);
        private static readonly System.Drawing.Color Border = System.Drawing.Color.FromArgb(63, 63, 70);
        private static readonly System.Drawing.Color Accent = System.Drawing.Color.FromArgb(10, 132, 255);

        public override System.Drawing.Color ToolStripDropDownBackground => Surface;
        public override System.Drawing.Color ImageMarginGradientBegin => Surface;
        public override System.Drawing.Color ImageMarginGradientMiddle => Surface;
        public override System.Drawing.Color ImageMarginGradientEnd => Surface;
        public override System.Drawing.Color MenuBorder => Border;
        public override System.Drawing.Color MenuItemBorder => Accent;
        public override System.Drawing.Color MenuItemSelected => Hover;
        public override System.Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override System.Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override System.Drawing.Color SeparatorDark => Border;
        public override System.Drawing.Color SeparatorLight => Surface;
    }
}
