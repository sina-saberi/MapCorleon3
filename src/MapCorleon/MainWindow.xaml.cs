using System.ComponentModel;
using System.Windows;
using MapCorleon.Services;
using MapCorleon.ViewModels;
using Microsoft.Extensions.Logging;
namespace MapCorleon;

public partial class MainWindow : Window
{
    readonly ShellViewModel vm;
    readonly ITrayService tray;
    readonly CancellationTokenSource overlayLifetime = new();
    readonly DesktopGameOverlay desktopOverlay;
    bool exitRequested;

    public MainWindow(ShellViewModel vm, ITrayService tray, ISettingsService settings, ILogger<MainWindow> overlayLogger)
    {
        InitializeComponent();
        DataContext = this.vm = vm;
        this.tray = tray;
        tray.Initialize(this);
        tray.ExitRequested += OnExitRequested;
        Closing += OnClosing;
        Loaded += async (_, _) => await vm.LoadAsync();
        var overlay = new SkyrimOverlayServer(Dispatcher, vm, DesktopMap.TogglePlayerFollow, settings, overlayLogger);
        _ = RunOverlayAsync(overlay);
        desktopOverlay = new DesktopGameOverlay(vm, DesktopMap.TogglePlayerFollow, settings, overlayLogger);
        SourceInitialized += (_, _) => desktopOverlay.Start(this);
    }

    private async Task RunOverlayAsync(SkyrimOverlayServer overlay)
    {
        try { await overlay.RunAsync(overlayLifetime.Token); }
        finally { overlayLifetime.Dispose(); }
    }

    protected override void OnClosed(EventArgs e)
    {
        desktopOverlay.Dispose();
        overlayLifetime.Cancel();
        base.OnClosed(e);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exitRequested)
            return;

        e.Cancel = true;
        Hide();
        tray.NotifyStillRunning();
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        exitRequested = true;
        Close();
        Application.Current.Shutdown();
    }
}
