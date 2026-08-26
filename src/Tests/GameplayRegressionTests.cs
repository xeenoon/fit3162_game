using Game;

namespace Game.Tests;

public sealed class GameplayRegressionTests
{
    [Fact]
    public void FailedPinSubmission_DoesNotOpenDoor()
    {
        var session = GameplaySessionTests.CreateSession(1);
        session.Interact(session.Dungeon.CellCenter(session.LockCell));

        var solved = session.FinishPinPuzzle();

        Assert.False(solved);
        Assert.False(session.DoorUnlocked);
        Assert.Equal(GameplayPhase.PinTumbler, session.Phase);
    }

    [Fact]
    public void WrongVaultDirection_ResetsCombinationProgress()
    {
        var session = GameplaySessionTests.CreateSession(2);
        session.Interact(session.Dungeon.CellCenter(session.LockCell));
        session.TurnVaultDial(DialDirection.Left);
        session.TurnVaultDial(DialDirection.Right);

        session.TurnVaultDial(DialDirection.Left);

        Assert.Equal(0, session.VaultPuzzle!.Progress);
        Assert.False(session.DoorUnlocked);
    }

    [Fact]
    public void Restart_ClosesGatesAndClearsDungeonInventory()
    {
        var session = GameplaySessionTests.CreateSession(1);
        session.Interact(session.Dungeon.CellCenter(session.LockCell));
        GameplaySessionTests.SolvePinPuzzle(session);

        session.Reset();

        Assert.False(session.DoorUnlocked);
        Assert.Empty(session.Inventory);
        Assert.False(session.Dungeon.CanOccupy(session.Dungeon.CellCenter(session.DoorCell), 4));
    }

    [Fact]
    public void ChestCannotCompleteBeforeLightPuzzle()
    {
        var session = GameplaySessionTests.CreateSession(1);

        session.Interact(session.Dungeon.CellCenter(session.ChestCell));

        Assert.Equal(GameplayPhase.Exploring, session.Phase);
        Assert.Equal(0, session.Stars);
    }
}
