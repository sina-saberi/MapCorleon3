using System.Windows;
using System.Windows.Controls;
using MapCorleon.Dialogs;
namespace MapCorleon.Views; public partial class LeftSidebarView : UserControl { public LeftSidebarView() => InitializeComponent(); void Progress_Click(object s, RoutedEventArgs e) => new ProgressDialog { Owner = Window.GetWindow(this), DataContext = DataContext }.ShowDialog(); void ClearSearch_Click(object s, RoutedEventArgs e) { SearchBox.Text = string.Empty; SearchBox.Focus(); } }
