using System.Text.Json;
using MapCorleon.Models;
using MapCorleon.ViewModels;

namespace MapCorleon.Tests;

public sealed class ProgressCategoriesViewModelTests
{
    [Fact]
    public void DraftUsesSavedSelectionsIndependentlyOfMapVisibility()
    {
        var settings = AppSettings.Default with { ExcludedProgressCategoryIds = [2] };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        var group = new Group
        {
            Name = "Places",
            Categories =
            [
                new Category { Id = 1, Name = "Cities", IsVisible = false },
                new Category { Id = 2, Name = "Caves" }
            ]
        };
        using var viewModel = new ProgressCategoriesViewModel([group], restored.ExcludedProgressCategoryIds);
        var choices = Assert.Single(viewModel.CategoryGroups).Categories;
        Assert.True(choices[0].IsIncluded);
        Assert.False(choices[1].IsIncluded);

        choices[0].IsIncluded = false;
        choices[1].IsIncluded = true;

        var selections = viewModel.GetSelections();
        Assert.False(selections[1]);
        Assert.True(selections[2]);
        Assert.Equal(new[] { 2 }, restored.ExcludedProgressCategoryIds);
        Assert.False(choices[0].Category.IsVisible);
        Assert.True(choices[1].Category.IsVisible);
    }
}
