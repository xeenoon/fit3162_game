using Game;
using Microsoft.Xna.Framework;

namespace Game.Tests;

public sealed class DungeonMapTests
{
    [Fact]
    public void FoundationDungeon_HasRouteFromStartToExit()
    {
        var dungeon = DungeonMap.CreateFoundationDungeon();

        Assert.True(dungeon.HasRouteFromStartToExit());
        Assert.True(dungeon.IsWalkable(dungeon.StartCell.X, dungeon.StartCell.Y));
        Assert.True(dungeon.IsWalkable(dungeon.ExitCell.X, dungeon.ExitCell.Y));
    }

    [Fact]
    public void PlayerMovement_NormalizesDiagonalInput()
    {
        var dungeon = OpenDungeon();
        var player = new PlayerController(dungeon.StartPosition, radius: 2, speed: 100);

        player.Update(new MovementInput(1, 1), 0.1f, dungeon);

        var travelled = Vector2.Distance(dungeon.StartPosition, player.Position);
        Assert.InRange(travelled, 9.99f, 10.01f);
    }

    [Fact]
    public void Restart_ReturnsPlayerToSpawn()
    {
        var dungeon = OpenDungeon();
        var player = new PlayerController(dungeon.StartPosition, radius: 2, speed: 100);
        player.Update(new MovementInput(1, 0), 0.1f, dungeon);

        player.Reset(dungeon.StartPosition);

        Assert.Equal(dungeon.StartPosition, player.Position);
    }

    private static DungeonMap OpenDungeon() => new(
    [
        "#######",
        "#S....#",
        "#.....#",
        "#....E#",
        "#######",
    ], cellSize: 20, origin: Point.Zero);
}
