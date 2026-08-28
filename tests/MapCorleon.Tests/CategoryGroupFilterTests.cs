using MapCorleon.Models;
using MapCorleon.ViewModels;

namespace MapCorleon.Tests;

public sealed class CategoryGroupFilterTests
{
    [Fact]
    public void CategoryMatchKeepsOnlyMatchingCategoriesAndTheirGroups()
    {
        var quests = CreateGroup("Quests", "Main quests", "Side quests");
        var places = CreateGroup("Places", "Cities", "Caves");

        var results = CategoryGroupFilter.Apply([quests, places], "side");

        var result = Assert.Single(results);
        Assert.Same(quests, result.Group);
        Assert.Equal("Side quests", Assert.Single(result.Categories).Name);
    }

    [Fact]
    public void GroupMatchKeepsAllCategoriesInTheGroup()
    {
        var places = CreateGroup("Places", "Cities", "Caves");

        var result = Assert.Single(CategoryGroupFilter.Apply([places], "place"));

        Assert.Equal(2, result.Categories.Count);
    }

    [Fact]
    public void GroupsWithoutMatchingCategoriesAreNotReturned()
    {
        var quests = CreateGroup("Quests", "Main quests");
        var empty = CreateGroup("Empty");

        Assert.Empty(CategoryGroupFilter.Apply([quests, empty], "cities"));
        Assert.DoesNotContain(CategoryGroupFilter.Apply([quests, empty], null), result => result.Group == empty);
    }

    private static Group CreateGroup(string name, params string[] categoryNames)
    {
        var group = new Group { Name = name };
        group.Categories = categoryNames
            .Select(categoryName => new Category { Name = categoryName, Group = group })
            .ToList();
        return group;
    }
}
