using System.Windows;
using MapCorleon.ViewModels;
namespace MapCorleon;

public partial class MainWindow : Window { readonly ShellViewModel vm; public MainWindow(ShellViewModel vm) { InitializeComponent(); DataContext = this.vm = vm; Loaded += async (_, _) => await vm.LoadAsync(); } }
