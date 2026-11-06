using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MapCorleon.Services;

namespace MapCorleon.Views;

internal partial class DesktopGameOverlayWindow : Window
{
    public static readonly DependencyProperty IsInteractiveProperty = DependencyProperty.Register(
        nameof(IsInteractive), typeof(bool), typeof(DesktopGameOverlayWindow));

    public bool IsInteractive => (bool)GetValue(IsInteractiveProperty);

    public event Action? ClosePanelRequested;
    public event Action<int>? CollectRequested;
    public event Action<int>? DismissRequested;

    public DesktopGameOverlayWindow() => InitializeComponent();

    public void Render(bool interactive, string? message, IReadOnlyList<OverlayNearbyLocations.Card> cards)
    {
        SetValue(IsInteractiveProperty, interactive);
        PanelHeader.Visibility = interactive ? Visibility.Visible : Visibility.Collapsed;
        MessageFrame.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        MessageText.Text = message;
        LocationCards.ItemsSource = cards;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        ClosePanelRequested?.Invoke();
    }

    private void ClosePanel_Click(object sender, RoutedEventArgs e) => ClosePanelRequested?.Invoke();
    private void Collect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OverlayNearbyLocations.Card card }) CollectRequested?.Invoke(card.Id);
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OverlayNearbyLocations.Card card }) DismissRequested?.Invoke(card.Id);
    }
}
