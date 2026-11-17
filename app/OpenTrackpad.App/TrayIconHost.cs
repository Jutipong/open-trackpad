using System.IO;
using WinForms = System.Windows.Forms;

namespace OpenTrackpad.App;

public sealed class TrayIconHost : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _appIcon;
    private readonly WinForms.ToolStripMenuItem _openItem;
    private readonly WinForms.ToolStripMenuItem _batteryItem;
    private readonly WinForms.ToolStripMenuItem _exitItem;
    private int? _batteryLevel;

    public TrayIconHost()
    {
        _openItem = new WinForms.ToolStripMenuItem(Loc.T("Tray.Open"));
        _batteryItem = new WinForms.ToolStripMenuItem(Loc.T("Tray.BatteryUnavailable"))
        {
            Enabled = false,
        };
        _exitItem = new WinForms.ToolStripMenuItem(Loc.T("Tray.Exit"));

        var menu = new WinForms.ContextMenuStrip
        {
            BackColor = System.Drawing.Color.FromArgb(37, 37, 40),
            ForeColor = System.Drawing.Color.FromArgb(232, 232, 232),
            Renderer = new WinForms.ToolStripProfessionalRenderer(new DarkMenuColorTable()),
        };
        _openItem.Click += (_, _) => OpenRequested?.Invoke();
        _exitItem.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(_openItem);
        menu.Items.Add(_batteryItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_exitItem);
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
        _batteryLevel = level;
        _batteryItem.Text = Loc.Format("Tray.Battery", level);
        _icon.Text = $"OpenTrackpad — {level}%";
    }

    public void SetBatteryUnavailable()
    {
        _batteryLevel = null;
        _batteryItem.Text = Loc.T("Tray.BatteryUnavailable");
        _icon.Text = "OpenTrackpad";
    }

    public void ApplyLanguage()
    {
        _openItem.Text = Loc.T("Tray.Open");
        _exitItem.Text = Loc.T("Tray.Exit");
        if (_batteryLevel is int level)
            SetBattery(level);
        else
            SetBatteryUnavailable();
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
