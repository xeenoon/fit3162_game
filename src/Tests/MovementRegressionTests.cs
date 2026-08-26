using Game;

namespace Game.Tests;

public sealed class MovementRegressionTests
{
    [Fact]
    public void LargeFrameDelta_DoesNotTunnelThroughWall()
    {
        var dungeon = new DungeonMap(
        [
            "#######",
            "#S#..E#",
            "#.#...#",
            "#.....#",
            "#######",
        ], cellSize: 20, origin: Microsoft.Xna.Framework.Point.Zero);
        var player = new PlayerController(dungeon.StartPosition, radius: 4, speed: 1000);

        player.Update(new MovementInput(1, 0), elapsedSeconds: 1f, dungeon);

        Assert.True(player.Position.X < dungeon.GetCellBounds(2, 1).Left);
        Assert.True(dungeon.CanOccupy(player.Position, player.Radius));
    }

    [Fact]
    public void CollisionSlidesAlongOpenAxis()
    {
        var dungeon = new DungeonMap(
        [
            "#######",
            "#S#..E#",
            "#.#...#",
            "#.....#",
            "#######",
        ], cellSize: 20, origin: Microsoft.Xna.Framework.Point.Zero);
        var player = new PlayerController(dungeon.StartPosition, radius: 4, speed: 100);

        player.Update(new MovementInput(1, 1), elapsedSeconds: 0.1f, dungeon);

        Assert.True(player.Position.Y > dungeon.StartPosition.Y);
        Assert.True(player.Position.X < dungeon.GetCellBounds(2, 1).Left);
    }
}
