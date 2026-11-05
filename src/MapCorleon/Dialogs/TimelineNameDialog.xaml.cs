using System.Windows;
using System.Windows.Controls;

namespace MapCorleon.Dialogs;

public partial class TimelineNameDialog : Window
{
    public TimelineNameDialog(string? name = null)
    {
        InitializeComponent();
        Title = name is null ? "Add timeline" : "Edit timeline";
        TimelineNameBox.Text = name ?? string.Empty;
        SaveButton.IsEnabled = TimelineName.Length > 0;
        Loaded += (_, _) =>
        {
            TimelineNameBox.Focus();
            TimelineNameBox.SelectAll();
        };
    }

    public string TimelineName => TimelineNameBox.Text.Trim();

    private void TimelineName_Changed(object sender, TextChangedEventArgs e)
    {
        if (SaveButton is not null)
            SaveButton.IsEnabled = TimelineName.Length > 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (TimelineName.Length > 0) DialogResult = true;
    }
}
