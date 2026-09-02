using Game;

namespace Game.Tests;

public sealed class WorldNavigationRegressionTests
{
    [Fact]
    public void DungeonSelection_WrapsAtBothEdges()
    {
        var navigation = new GameNavigation();
        navigation.Confirm();

        navigation.MovePrevious();
        Assert.Equal(WorldGenerator.DungeonCount - 1, navigation.DungeonIndex);

        navigation.MoveNext();
        Assert.Equal(0, navigation.DungeonIndex);
    }

    [Fact]
    public void LeavingGameplay_ReturnsToTheSelectedDungeon()
    {
        var navigation = new GameNavigation();
        navigation.Confirm();
        navigation.MoveNext();
        navigation.Confirm();

        navigation.Back();

        Assert.Equal(AppScreen.WorldMap, navigation.Screen);
        Assert.Equal(1, navigation.DungeonIndex);
    }
}
