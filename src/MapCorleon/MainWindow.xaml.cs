using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using MapCorleon.Services;
using MapCorleon.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace MapCorleon;

public partial class MainWindow : Window
{
    readonly ShellViewModel vm;
    readonly ITrayService tray;
    readonly CancellationTokenSource overlayLifetime = new();
    readonly DesktopGameOverlay desktopOverlay;
    readonly DispatcherTimer saveWindowTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    string? windowSessionKey;
    bool exitRequested;

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && App.Services.GetRequiredService<IMapPickService>().Cancel())
            e.Handled = true;
        base.OnPreviewKeyDown(e);
    }

    public MainWindow(ShellViewModel vm, ITrayService tray, ISettingsService settings, ILogger<MainWindow> overlayLogger)
    {
        InitializeComponent();
        DataContext = this.vm = vm;
        this.tray = tray;
        tray.Initialize(this);
        tray.ExitRequested += OnExitRequested;
        Closing += OnClosing;
        vm.PropertyChanged += ViewModelChanged;
        saveWindowTimer.Tick += (_, _) => SaveWindowPlacement();
        LocationChanged += (_, _) => QueueWindowSave();
        SizeChanged += (_, _) => QueueWindowSave();
        StateChanged += (_, _) => QueueWindowSave();
        Loaded += async (_, _) => await vm.LoadAsync();
        var overlay = new SkyrimOverlayServer(Dispatcher, vm, DesktopMap.TogglePlayerFollow, settings, overlayLogger);
        _ = RunOverlayAsync(overlay);
        desktopOverlay = new DesktopGameOverlay(vm, DesktopMap.TogglePlayerFollow, settings, overlayLogger);
        SourceInitialized += (_, _) => desktopOverlay.Start(this);
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.CurrentMap) || windowSessionKey == vm.MapSessionKey)
            return;

        SaveWindowPlacement();
        var key = windowSessionKey = vm.MapSessionKey;
        if (vm.MapSessions.Read(key).Window is not { } saved) return;

        Dispatcher.BeginInvoke(() =>
        {
            if (key == windowSessionKey && IsVisible)
                WindowPlacement.Restore(new WindowInteropHelper(this).Handle, saved);
        });
    }

    private void QueueWindowSave()
    {
        if (windowSessionKey is null || !IsVisible) return;
        saveWindowTimer.Stop();
        saveWindowTimer.Start();
    }

    private void SaveWindowPlacement()
    {
        saveWindowTimer.Stop();
        if (windowSessionKey is not { } key || !IsVisible || WindowState == WindowState.Minimized ||
            WindowPlacement.Capture(new WindowInteropHelper(this).Handle) is not { } placement)
            return;

        vm.MapSessions.Update(key, state => state with { Window = placement });
    }

    private async Task RunOverlayAsync(SkyrimOverlayServer overlay)
    {
        try { await overlay.RunAsync(overlayLifetime.Token); }
        finally { overlayLifetime.Dispose(); }
    }

    protected override void OnClosed(EventArgs e)
    {
        saveWindowTimer.Stop();
        vm.PropertyChanged -= ViewModelChanged;
        desktopOverlay.Dispose();
        overlayLifetime.Cancel();
        base.OnClosed(e);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        DesktopMap.SaveViewport();
        SaveWindowPlacement();
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
