using Game;
using System.Linq;

namespace Game.Tests;

public sealed class WorldSelectionTests
{
    [Fact]
    public void SameSeed_ReproducesDungeonChoices()
    {
        var first = WorldGenerator.Generate(2026);
        var second = WorldGenerator.Generate(2026);

        Assert.Equal(first.Dungeons, second.Dungeons);
    }

    [Fact]
    public void GeneratedWorld_TransitionsFromTwoHillsDungeonsToMountainDungeon()
    {
        var world = WorldGenerator.Generate(42);

        Assert.Equal(3, world.Dungeons.Count);
        Assert.Equal([Biome.Hills, Biome.Hills, Biome.Mountain], world.Dungeons.Select(dungeon => dungeon.Biome));
    }

    [Fact]
    public void NewSeeds_ProduceDifferentTerrain()
    {
        var firstHeights = Enumerable.Range(1, 8)
            .Select(seed => WorldGenerator.Generate(seed).Terrain.Heights[1200])
            .Distinct();

        Assert.True(firstHeights.Count() > 1);
    }

    [Fact]
    public void Navigation_VisitsInstructionsAndReturnsToMenu()
    {
        var navigation = new GameNavigation();

        navigation.MoveNext();
        Assert.Equal("HOW TO PLAY", navigation.SelectedMenuOption);
        navigation.Confirm();
        Assert.Equal(AppScreen.HowToPlay, navigation.Screen);
        navigation.Back();
        Assert.Equal(AppScreen.MainMenu, navigation.Screen);
    }

    [Fact]
    public void Navigation_SelectsDungeonAndStartsGameplay()
    {
        var navigation = new GameNavigation();

        navigation.Confirm();
        navigation.MoveNext();
        navigation.Confirm();

        Assert.Equal(1, navigation.DungeonIndex);
        Assert.Equal(AppScreen.Gameplay, navigation.Screen);
    }
}
