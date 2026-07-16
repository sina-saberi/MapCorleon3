using System.ComponentModel;
using System.Windows;
using MapCorleon.Services;
using MapCorleon.ViewModels;
namespace MapCorleon;

public partial class MainWindow : Window
{
    readonly ShellViewModel vm;
    readonly ITrayService tray;
    bool exitRequested;

    public MainWindow(ShellViewModel vm, ITrayService tray)
    {
        InitializeComponent();
        DataContext = this.vm = vm;
        this.tray = tray;
        tray.Initialize(this);
        tray.ExitRequested += OnExitRequested;
        Closing += OnClosing;
        Loaded += async (_, _) => await vm.LoadAsync();
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
