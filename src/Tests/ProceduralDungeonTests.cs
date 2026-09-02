using System;
using System.Linq;
using Game;

namespace Game.Tests;

public sealed class ProceduralDungeonTests
{
    [Fact]
    public void SameSeed_ReproducesLayoutRoomsAndDecorations()
    {
        var first = DungeonMap.CreateProceduralDungeon(2026, 2);
        var second = DungeonMap.CreateProceduralDungeon(2026, 2);

        Assert.Equal(first.LayoutFingerprint(), second.LayoutFingerprint());
        Assert.Equal(first.Rooms, second.Rooms);
        Assert.Equal(first.Decorations, second.Decorations);
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentMazeLayouts()
    {
        var first = DungeonMap.CreateProceduralDungeon(2026, 1);
        var second = DungeonMap.CreateProceduralDungeon(2027, 1);

        Assert.NotEqual(first.LayoutFingerprint(), second.LayoutFingerprint());
    }

    [Fact]
    public void GeneratedMaze_ContainsFourRectangularRooms()
    {
        var dungeon = DungeonMap.CreateProceduralDungeon(2026, 1);

        Assert.Equal(4, dungeon.Rooms.Count);
        Assert.All(dungeon.Rooms, room =>
        {
            Assert.InRange(room.Width, 5, 9);
            Assert.InRange(room.Height, 3, 5);
            for (var row = room.Y; row < room.Y + room.Height; row++)
            {
                for (var column = room.X; column < room.X + room.Width; column++)
                {
                    Assert.True(dungeon.IsWalkable(column, row));
                }
            }
        });
    }

    [Fact]
    public void RoomDecorations_IncludeEveryRequestedDetailType()
    {
        var dungeon = DungeonMap.CreateProceduralDungeon(2026, 1);

        var features = dungeon.Decorations.Select(item => item.Feature).Distinct();
        Assert.Equal(Enum.GetValues<DungeonFeature>().Order(), features.Order());
        Assert.All(dungeon.Decorations, item =>
        {
            Assert.Contains(dungeon.Rooms, room => room.Contains(item.Cell));
            Assert.True(dungeon.IsWalkable(item.Cell.X, item.Cell.Y));
            Assert.NotEqual(dungeon.StartCell, item.Cell);
            Assert.NotEqual(dungeon.ExitCell, item.Cell);
        });
    }
}
