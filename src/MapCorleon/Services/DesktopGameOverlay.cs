using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using MapCorleon.Models;
using MapCorleon.ViewModels;
using MapCorleon.Views;
using Microsoft.Extensions.Logging;
using static MapCorleon.Services.DesktopGameOverlayNative;

namespace MapCorleon.Services;

internal sealed class DesktopGameOverlay : IDisposable
{
    private const int PanelHotkey = 0x5401;
    private const int FollowHotkey = PanelHotkey + 1;
    private readonly ShellViewModel shell;
    private readonly Func<bool> toggleFollow;
    private readonly ISettingsService settings;
    private readonly ILogger logger;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource stopping = new();
    private readonly OverlayNearbyLocations nearby = new();
    private readonly OverlayNearbyAlerts alerts = new();
    private HwndSource? source;
    private DesktopGameOverlayWindow? view;
    private nint overlayWindow;
    private nint gameWindow;
    private uint foregroundProcessId;
    private bool foregroundMatchesGame;
    private DesktopOverlayProfile? activeGame;
    private bool panelOpen;
    private bool collecting;
    private (uint Panel, uint Follow)? registeredKeys;
    private (int MapId, int TimelineId)? scope;
    private IReadOnlyList<OverlayNearbyLocations.Card> cards = [];
    private IReadOnlyList<OverlayNearbyLocations.Card> displayedCards = [];
    private bool displayedPanel;
    private string? displayedMessage;
    private string? feedback;
    private TimeSpan feedbackUntil;

    public DesktopGameOverlay(ShellViewModel shell, Func<bool> toggleFollow, ISettingsService settings, ILogger logger)
    {
        this.shell = shell;
        this.toggleFollow = toggleFollow;
        this.settings = settings;
        this.logger = logger;
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) => Refresh();
    }

    public void Start(Window owner)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(owner).Handle);
        if (source is null) return;
        source.AddHook(OnWindowMessage);
        timer.Start();
    }

    private void Refresh()
    {
        var map = shell.CurrentMap;
        var profile = map is null ? null : DesktopOverlayProfile.ForMap(GameMapDescriptorFactory.Create(map));
        var activeScope = profile is null ? null : shell.LoadedLocationScope;
        if (scope != activeScope)
        {
            scope = activeScope;
            activeGame = profile?.Game;
            foregroundProcessId = 0;
            nearby.Reset();
            alerts.Reset();
            cards = [];
            feedback = null;
            ClosePanel();
        }

        var foreground = GetForegroundWindow();
        if (scope is null || profile is not { } mapProfile || !TryGetTarget(foreground, mapProfile.Game) ||
            !TryGetGameAnchor(gameWindow, out var anchor))
        {
            SetHotkeys(null);
            panelOpen = false;
            view?.Hide();
            return;
        }

        var preferences = mapProfile.Game.GetSettings(settings);
        var panel = VirtualKey(preferences.PanelScanCode, 0x76);
        var follow = VirtualKey(preferences.FollowScanCode, 0x77);
        SetHotkeys(panel != follow ? (panel, follow) : (0x76u, 0x77u));

        cards = nearby.Find(shell.LivePlayerLocation, shell.LoadedLocations, preferences,
            mapProfile.DistanceSquared, int.MaxValue,
            Math.Clamp(preferences.NearbyRadius, mapProfile.Game.MinRadius, mapProfile.Game.MaxRadius));
        var now = clock.Elapsed;
        var duration = TimeSpan.FromSeconds(Math.Clamp(preferences.NearbyNotificationSeconds,
            GameOverlaySettings.MinNotificationSeconds, GameOverlaySettings.MaxNotificationSeconds));
        var toasts = alerts.Update(cards, now, duration);
        var message = now < feedbackUntil ? feedback : null;
        if (panelOpen)
        {
            if (cards.Count == 0 && message is null)
                message = shell.LivePlayerLocation is null ? shell.PlayerTrackingStatusText : "No nearby unmarked locations.";
            Display(anchor, true, message, cards.Take(5).ToArray());
        }
        else if (toasts.Count > 0 || message is not null)
        {
            Display(anchor, false, message, toasts);
        }
        else
        {
            view?.Hide();
        }
    }

    private bool TryGetTarget(nint foreground, DesktopOverlayProfile game)
    {
        if (panelOpen && foreground != 0 && foreground == overlayWindow) return true;
        if (foreground == 0) return false;
        GetWindowThreadProcessId(foreground, out var processId);
        if (foregroundProcessId != processId)
        {
            foregroundProcessId = processId;
            foregroundMatchesGame = MatchesGameProcess(processId, game);
        }
        if (!foregroundMatchesGame) return false;
        if (gameWindow != foreground)
        {
            gameWindow = foreground;
            alerts.Reset();
            nearby.Reset();
        }
        return true;
    }

    private static bool MatchesGameProcess(uint processId, DesktopOverlayProfile game)
    {
        if (processId == 0) return false;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName.Equals(game.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                (game.ExecutableVersion is null || process.MainModule?.FileVersionInfo.FileVersion == game.ExecutableVersion);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private void Display(DesktopGameOverlayNative.Point anchor, bool interactive, string? message, IReadOnlyList<OverlayNearbyLocations.Card> content)
    {
        if (view is null)
        {
            view = new DesktopGameOverlayWindow();
            view.ClosePanelRequested += () => { ClosePanel(); Refresh(); };
            view.CollectRequested += Collect;
            view.DismissRequested += id =>
            {
                if (!panelOpen || scope != shell.LoadedLocationScope || !cards.Any(card => card.Id == id)) return;
                nearby.Dismiss(id);
                Refresh();
            };
            overlayWindow = new WindowInteropHelper(view).EnsureHandle();
            SetPassive(overlayWindow, !interactive);
        }
        if (displayedPanel != interactive)
            SetPassive(overlayWindow, !interactive);
        if (displayedPanel != interactive || displayedMessage != message || !displayedCards.SequenceEqual(content))
        {
            view.Render(interactive, message, content);
            displayedPanel = interactive;
            displayedMessage = message;
            displayedCards = content;
        }
        if (!view.IsVisible) view.Show();
        MoveToAnchor(overlayWindow, anchor);
    }

    private void SetHotkeys((uint Panel, uint Follow)? keys)
    {
        if (registeredKeys == keys || source is null) return;
        if (registeredKeys is not null)
        {
            UnregisterHotKey(source.Handle, PanelHotkey);
            UnregisterHotKey(source.Handle, FollowHotkey);
        }
        registeredKeys = keys;
        if (keys is not { } bindings) return;
        if (!RegisterHotKey(source.Handle, PanelHotkey, NoRepeat, bindings.Panel))
            logger.LogWarning("{Game} overlay panel hotkey could not be registered. Win32 error {Error}.", activeGame?.Name,
                System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        if (!RegisterHotKey(source.Handle, FollowHotkey, NoRepeat, bindings.Follow))
            logger.LogWarning("{Game} Follow Player hotkey could not be registered. Win32 error {Error}.", activeGame?.Name,
                System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }

    private nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != HotkeyMessage || registeredKeys is null || scope is null) return 0;
        var id = (int)wParam;
        if (id is not (PanelHotkey or FollowHotkey)) return 0;
        handled = true;
        var foreground = GetForegroundWindow();
        if (foreground != gameWindow && !(panelOpen && foreground == overlayWindow)) return 0;
        if (shell.LoadedLocationScope != scope || !TryGetGameAnchor(gameWindow, out _)) return 0;

        if (id == FollowHotkey)
        {
            var messageText = shell.LivePlayerLocation is null
                ? "Player location unavailable"
                : toggleFollow() ? "Follow Player on" : "Follow Player off";
            ShowFeedback(messageText);
        }
        else if (panelOpen)
        {
            ClosePanel();
            Refresh();
        }
        else
        {
            panelOpen = true;
            Refresh();
            if (panelOpen) view?.Activate();
        }
        return 0;
    }

    private void ClosePanel()
    {
        if (!panelOpen) return;
        panelOpen = false;
        if (GetForegroundWindow() == overlayWindow && TryGetGameAnchor(gameWindow, out _))
            SetForegroundWindow(gameWindow);
        view?.Hide();
    }

    private async void Collect(int id)
    {
        if (!panelOpen || collecting || scope is not { } target || !cards.Any(card => card.Id == id)) return;
        collecting = true;
        try
        {
            var collected = await shell.CollectOverlayLocationAsync(id, target, stopping.Token);
            if (!stopping.IsCancellationRequested && scope == target)
                ShowFeedback(collected ? "Collected" : "Could not collect. Try again.");
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Desktop overlay could not collect location {LocationId}.", id);
            if (!stopping.IsCancellationRequested && scope == target) ShowFeedback("Could not collect. Try again.");
        }
        finally { collecting = false; }
    }

    private void ShowFeedback(string message)
    {
        feedback = message;
        feedbackUntil = clock.Elapsed + TimeSpan.FromSeconds(2);
        Refresh();
    }

    private static uint VirtualKey(int? scanCode, uint fallback) => scanCode switch
    {
        >= 0x3B and <= 0x44 => (uint)(0x70 + scanCode.Value - 0x3B),
        0x57 => 0x7A,
        _ => fallback
    };

    public void Dispose()
    {
        timer.Stop();
        if (source is { IsDisposed: false })
        {
            SetHotkeys(null);
            source.RemoveHook(OnWindowMessage);
        }
        ClosePanel();
        stopping.Cancel();
        view?.Close();
        stopping.Dispose();
    }
}
