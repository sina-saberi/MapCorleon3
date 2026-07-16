using System.Windows;
using System.Windows.Controls;
using MapCorleon.Dialogs;
namespace MapCorleon.Views; public partial class HeaderView : UserControl { public HeaderView() => InitializeComponent(); Window Owner => Window.GetWindow(this); void Settings_Click(object s, RoutedEventArgs e) => new SettingsDialog { Owner = Owner }.ShowDialog(); void Import_Click(object s, RoutedEventArgs e) => new ImportDialog { Owner = Owner }.ShowDialog(); void Timeline_Click(object s, RoutedEventArgs e) => new TimelineDialog { Owner = Owner, DataContext = DataContext }.ShowDialog(); void Custom_Click(object s, RoutedEventArgs e) => new LocationEditorDialog { Owner = Owner, DataContext = DataContext }.Show(); }
