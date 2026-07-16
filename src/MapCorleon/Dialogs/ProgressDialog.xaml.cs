using System.Windows;
namespace MapCorleon.Dialogs; public partial class ProgressDialog : Window { public ProgressDialog() => InitializeComponent(); void Apply_Click(object s, RoutedEventArgs e) => DialogResult = true; }
