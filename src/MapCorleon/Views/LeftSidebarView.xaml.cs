using System.Windows;
using System.Windows.Controls;
using MapCorleon.Dialogs;
using MapCorleon.ViewModels;

namespace MapCorleon.Views;

public partial class LeftSidebarView : UserControl
{
    public LeftSidebarView() => InitializeComponent();

    private void Progress_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell) return;

        new ProgressDialog(shell.Groups)
        {
            Owner = Window.GetWindow(this)
        }.ShowDialog();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        SearchBox.Focus();
    }
}
