using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace MapCorleon.Services;

public interface ITrayService : IDisposable
{
    event EventHandler? ExitRequested;
    void Initialize(Window window);
    void ShowWindow();
    void NotifyStillRunning();
}

public sealed class TrayService : ITrayService
{
    private Forms.NotifyIcon? icon;
    private Window? window;
    private bool hasNotified;

    public event EventHandler? ExitRequested;

    public void Initialize(Window target)
    {
        window = target;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show MapCorleon", null, (_, _) => ShowWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        icon = new Forms.NotifyIcon
        {
            Text = "MapCorleon",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        icon.DoubleClick += (_, _) => ShowWindow();
    }

    public void ShowWindow()
    {
        if (window is null)
            return;

        window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }

    public void NotifyStillRunning()
    {
        if (icon is null || hasNotified)
            return;

        hasNotified = true;
        icon.ShowBalloonTip(
            3000,
            "MapCorleon is still running",
            "Use the tray icon to reopen the app or choose Exit to stop it.",
            Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (icon is null)
            return;

        icon.Visible = false;
        icon.ContextMenuStrip?.Dispose();
        icon.Dispose();
        icon = null;
    }
}
