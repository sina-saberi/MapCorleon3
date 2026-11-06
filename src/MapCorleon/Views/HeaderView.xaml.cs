using System.Windows.Controls;
using MapCorleon.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MapCorleon.Views;

public partial class HeaderView : UserControl
{
    public HeaderView()
    {
        InitializeComponent();
        TaskPanel.DataContext = App.Services.GetRequiredService<IImportTaskService>();
    }
}
