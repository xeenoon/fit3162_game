using Game;
using Microsoft.Xna.Framework;

namespace Game.Tests;

public sealed class GameplaySessionTests
{
    [Fact]
    public void PinTumbler_UnlocksDoorAndAwardsKey()
    {
        var session = CreateSession(1);
        session.Interact(session.Dungeon.CellCenter(session.LockCell));

        SolvePinPuzzle(session);

        Assert.Equal(GameplayPhase.Exploring, session.Phase);
        Assert.True(session.DoorUnlocked);
        Assert.Contains("BRASS KEY", session.Inventory);
        Assert.True(session.Dungeon.CanOccupy(session.Dungeon.CellCenter(session.DoorCell), 4));
    }

    [Fact]
    public void Switch_RespondsToElementInteraction()
    {
        var session = CreateSession(1);

        session.Interact(session.Dungeon.CellCenter(session.SwitchCell));

        Assert.True(session.SwitchActivated);
        Assert.StartsWith("SWITCH ACTIVATED", session.Status);
    }

    [Fact]
    public void InteractionPrompt_TracksTheNearestEligibleElement()
    {
        var session = CreateSession(1);
        var npcCenter = session.Dungeon.CellCenter(session.NpcCell);

        Assert.Equal(session.NpcCell, session.NearestInteractionCell(npcCenter));
        session.Interact(npcCenter);

        Assert.Equal(session.SwitchCell, session.NearestInteractionCell(npcCenter));
    }

    [Fact]
    public void VaultDial_IsASecondPlayableLockVariant()
    {
        var session = CreateSession(2);
        session.Interact(session.Dungeon.CellCenter(session.LockCell));

        session.TurnVaultDial(DialDirection.Left);
        session.TurnVaultDial(DialDirection.Right);
        session.TurnVaultDial(DialDirection.Up);
        session.TurnVaultDial(DialDirection.Down);

        Assert.Equal(GameplayPhase.Exploring, session.Phase);
        Assert.True(session.DoorUnlocked);
    }

    [Fact]
    public void MirrorPuzzle_OpensLightGateAndAwardsShard()
    {
        var session = CreateSession(1);
        session.Interact(session.Dungeon.CellCenter(session.LockCell));
        SolvePinPuzzle(session);
        session.Interact(session.Dungeon.CellCenter(session.LightCell));

        session.LightPuzzle!.Toggle();
        session.LightPuzzle.Move(1);
        session.LightPuzzle.Move(1);
        session.LightPuzzle.Toggle();
        session.FinishLightPuzzle();

        Assert.True(session.LightSolved);
        Assert.Contains("LUMEN SHARD", session.Inventory);
        Assert.True(session.Dungeon.CanOccupy(session.Dungeon.CellCenter(session.LightGateCell), 4));
    }

    [Fact]
    public void Completion_AwardsAllThreeStarsWhenKeeperWasFound()
    {
        var session = CreateSession(1);
        session.Interact(session.Dungeon.CellCenter(session.NpcCell));
        session.Interact(session.Dungeon.CellCenter(session.LockCell));
        SolvePinPuzzle(session);
        session.Interact(session.Dungeon.CellCenter(session.LightCell));
        SolveLightPuzzle(session);

        session.Interact(session.Dungeon.CellCenter(session.ChestCell));

        Assert.Equal(GameplayPhase.Completed, session.Phase);
        Assert.Equal(3, session.Stars);
    }

    [Fact]
    public void PitAndWatcher_BothTriggerGameOver()
    {
        var pitSession = CreateSession(1);
        var safeWatcher = new Vector2(10000, 10000);
        pitSession.CheckHazards(pitSession.Dungeon.CellCenter(pitSession.PitCell), safeWatcher);

        var watcherSession = CreateSession(1);
        var player = watcherSession.Dungeon.StartPosition;
        watcherSession.CheckHazards(player, player + new Vector2(8, 0));

        Assert.Equal(GameOverReason.Pit, pitSession.GameOverReason);
        Assert.Equal(GameOverReason.Watcher, watcherSession.GameOverReason);
    }

    internal static GameplaySession CreateSession(int number) =>
        new(DungeonMap.CreateFoundationDungeon(), number);

    internal static void SolvePinPuzzle(GameplaySession session)
    {
        session.PinPuzzle!.Adjust(1);
        session.PinPuzzle.Move(1);
        session.PinPuzzle.Adjust(1);
        session.PinPuzzle.Adjust(1);
        session.PinPuzzle.Move(1);
        session.PinPuzzle.Adjust(1);
        Assert.True(session.FinishPinPuzzle());
    }

    internal static void SolveLightPuzzle(GameplaySession session)
    {
        session.LightPuzzle!.Toggle();
        session.LightPuzzle.Move(1);
        session.LightPuzzle.Move(1);
        session.LightPuzzle.Toggle();
        Assert.True(session.FinishLightPuzzle());
    }
}
