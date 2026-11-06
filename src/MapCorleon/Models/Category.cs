using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

namespace MapCorleon.Models;

public sealed class Category : IEntity, INotifyPropertyChanged
{
    private bool isVisible = true;
    private int totalLocationCount;
    private int pickedLocationCount;
    public int Id { get; set; }
    public int GroupId { get; set; }
    public long? ExternalId { get; set; }
    public required string Name { get; set; }
    public bool IsVisible
    {
        get => isVisible;
        set
        {
            if (isVisible == value) return;
            isVisible = value;
            OnPropertyChanged();
        }
    }
    public string? IconKey { get; set; }
    public int? IconX { get; set; }
    public int? IconY { get; set; }
    public int? IconWidth { get; set; }
    public int? IconHeight { get; set; }
    public double? IconPixelRatio { get; set; }
    [NotMapped]
    public int TotalLocationCount
    {
        get => totalLocationCount;
        private set { if (totalLocationCount == value) return; totalLocationCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); }
    }
    [NotMapped]
    public int PickedLocationCount
    {
        get => pickedLocationCount;
        private set { if (pickedLocationCount == value) return; pickedLocationCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); }
    }
    [NotMapped]
    public string ProgressText => $"{PickedLocationCount}/{TotalLocationCount}";

    public Group? Group { get; set; }
    public ICollection<Location> Locations { get; set; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;
    public void SetProgress(int total, int picked)
    {
        TotalLocationCount = total;
        PickedLocationCount = picked;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
