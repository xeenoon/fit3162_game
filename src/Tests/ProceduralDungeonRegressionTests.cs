using Game;

namespace Game.Tests;

public sealed class ProceduralDungeonRegressionTests
{
    [Fact]
    public void KnownSeed_KeepsStableRoomPlanAcrossProcesses()
    {
        var dungeon = DungeonMap.CreateProceduralDungeon(2026, 2);

        Assert.Equal(new DungeonRoom(3, 3, 9, 4), dungeon.Rooms[0]);
        Assert.Equal(new DungeonRoom(17, 2, 9, 4), dungeon.Rooms[1]);
        Assert.Equal(new DungeonRoom(3, 10, 8, 3), dungeon.Rooms[2]);
        Assert.Equal(new DungeonRoom(18, 8, 7, 5), dungeon.Rooms[3]);
    }

    [Fact]
    public void GeneratedDungeons_RemainSolvableAcrossSeedSample()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            for (var variant = 1; variant <= WorldGenerator.DungeonCount; variant++)
            {
                var dungeon = DungeonMap.CreateProceduralDungeon(seed, variant);

                Assert.True(dungeon.HasRouteFromStartToExit(), $"seed {seed}, variant {variant}");
            }
        }
    }

    [Fact]
    public void SpawnAlwaysHasAnEastwardGameplayGallery()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            var dungeon = DungeonMap.CreateProceduralDungeon(seed, 1);

            for (var column = 1; column <= 19; column++)
            {
                Assert.True(dungeon.IsWalkable(column, 1));
            }
        }
    }
}
