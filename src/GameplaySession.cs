using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Game;

public enum GameplayPhase
{
    Exploring,
    PinTumbler,
    VaultDial,
    LightPuzzle,
    Completed,
    GameOver,
}

public enum GameOverReason
{
    None,
    Pit,
    Watcher,
}

public enum DialDirection
{
    Left,
    Right,
    Up,
    Down,
}

public sealed class PinTumblerPuzzle
{
    private static readonly int[] Target = [1, 2, 1];
    private readonly int[] _heights = new int[Target.Length];

    public int SelectedPin { get; private set; }
    public IReadOnlyList<int> Heights => _heights;
    public IReadOnlyList<int> TargetHeights => Target;
    public bool IsSolved { get; private set; }

    public void Move(int direction) => SelectedPin = Math.Clamp(SelectedPin + direction, 0, _heights.Length - 1);

    public void Adjust(int direction)
    {
        _heights[SelectedPin] = (_heights[SelectedPin] + direction + 4) % 4;
    }

    public bool Submit()
    {
        IsSolved = _heights.SequenceEqual(Target);
        return IsSolved;
    }
}

public sealed class VaultDialPuzzle
{
    private static readonly DialDirection[] Combination =
        [DialDirection.Left, DialDirection.Right, DialDirection.Up, DialDirection.Down];

    public int Progress { get; private set; }
    public int StepCount => Combination.Length;
    public bool IsSolved => Progress == Combination.Length;
    public IReadOnlyList<DialDirection> TargetCombination => Combination;

    public bool Turn(DialDirection direction)
    {
        if (IsSolved)
        {
            return true;
        }

        Progress = direction == Combination[Progress] ? Progress + 1 : 0;
        return IsSolved;
    }
}

public sealed class MirrorLightPuzzle
{
    private static readonly bool[] Target = [true, false, true];
    private readonly bool[] _orientations = new bool[Target.Length];

    public int SelectedMirror { get; private set; }
    public IReadOnlyList<bool> Orientations => _orientations;
    public IReadOnlyList<bool> TargetOrientations => Target;
    public bool IsSolved { get; private set; }

    public void Move(int direction) =>
        SelectedMirror = Math.Clamp(SelectedMirror + direction, 0, _orientations.Length - 1);

    public void Toggle() => _orientations[SelectedMirror] = !_orientations[SelectedMirror];

    public bool Submit()
    {
        IsSolved = _orientations.SequenceEqual(Target);
        return IsSolved;
    }
}

public sealed class GameplaySession
{
    private const float InteractionRange = 46f;
    private readonly HashSet<string> _inventory = [];

    public GameplaySession(DungeonMap dungeon, int dungeonNumber)
    {
        Dungeon = dungeon;
        DungeonNumber = dungeonNumber;
        Reset();
    }

    public DungeonMap Dungeon { get; }
    public int DungeonNumber { get; }
    public Point NpcCell { get; } = new(3, 1);
    public Point SwitchCell { get; } = new(4, 1);
    public Point LockCell { get; } = new(5, 1);
    public Point DoorCell { get; } = new(7, 1);
    public Point LightCell { get; } = new(9, 1);
    public Point LightGateCell { get; } = new(11, 1);
    public Point ChestCell { get; } = new(13, 1);
    public Point PitCell { get; } = new(1, 2);
    public GameplayPhase Phase { get; private set; }
    public GameOverReason GameOverReason { get; private set; }
    public bool DoorUnlocked { get; private set; }
    public bool LightSolved { get; private set; }
    public bool HiddenObjective { get; private set; }
    public bool SwitchActivated { get; private set; }
    public bool WasCaught { get; private set; }
    public int Stars { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public IReadOnlyCollection<string> Inventory => _inventory;
    public PinTumblerPuzzle? PinPuzzle { get; private set; }
    public VaultDialPuzzle? VaultPuzzle { get; private set; }
    public MirrorLightPuzzle? LightPuzzle { get; private set; }

    public void Reset()
    {
        Dungeon.ClearBlockers();
        Dungeon.SetBlocked(DoorCell, true);
        Dungeon.SetBlocked(LightGateCell, true);
        _inventory.Clear();
        Phase = GameplayPhase.Exploring;
        GameOverReason = GameOverReason.None;
        DoorUnlocked = false;
        LightSolved = false;
        HiddenObjective = false;
        SwitchActivated = false;
        WasCaught = false;
        Stars = 0;
        Status = "FIND CLUES AND OPEN THE VAULT";
        PinPuzzle = null;
        VaultPuzzle = null;
        LightPuzzle = null;
    }

    public void Interact(Vector2 playerPosition)
    {
        if (Phase != GameplayPhase.Exploring)
        {
            return;
        }

        var nearest = NearestTarget(playerPosition);

        if (nearest == default)
        {
            Status = "NOTHING TO INTERACT WITH";
            return;
        }

        if (nearest.Target == InteractionTarget.Npc)
        {
            HiddenObjective = true;
            Status = "HIDDEN OBJECTIVE  LISTENED TO THE KEEPER";
            return;
        }

        if (nearest.Target == InteractionTarget.Switch)
        {
            SwitchActivated = true;
            Status = "SWITCH ACTIVATED  SECRET PATH MARKED";
            return;
        }

        if (nearest.Target == InteractionTarget.Lock)
        {
            if (DungeonNumber % 2 == 1)
            {
                PinPuzzle = new PinTumblerPuzzle();
                Phase = GameplayPhase.PinTumbler;
                Status = "MATCH THE PIN HEIGHTS";
            }
            else
            {
                VaultPuzzle = new VaultDialPuzzle();
                Phase = GameplayPhase.VaultDial;
                Status = "ENTER THE DIRECTION SEQUENCE";
            }

            return;
        }

        if (nearest.Target == InteractionTarget.Light)
        {
            LightPuzzle = new MirrorLightPuzzle();
            Phase = GameplayPhase.LightPuzzle;
            Status = "ALIGN THE MIRRORS";
            return;
        }

        if (nearest.Target == InteractionTarget.Chest)
        {
            if (!LightSolved)
            {
                Status = "THE CHEST IS SEALED BY DARKNESS";
                return;
            }

            Phase = GameplayPhase.Completed;
            Stars = 1 + (WasCaught ? 0 : 1) + (HiddenObjective ? 1 : 0);
            Status = "DUNGEON COMPLETE";
            return;
        }

    }

    public Point? NearestInteractionCell(Vector2 playerPosition)
    {
        var nearest = NearestTarget(playerPosition);
        return nearest == default ? null : nearest.Cell;
    }

    public bool FinishPinPuzzle()
    {
        if (Phase != GameplayPhase.PinTumbler || PinPuzzle is null || !PinPuzzle.Submit())
        {
            Status = "THE PINS DO NOT ALIGN";
            return false;
        }

        UnlockFirstDoor();
        return true;
    }

    public bool TurnVaultDial(DialDirection direction)
    {
        if (Phase != GameplayPhase.VaultDial || VaultPuzzle is null)
        {
            return false;
        }

        if (!VaultPuzzle.Turn(direction))
        {
            Status = $"DIAL SEQUENCE  {VaultPuzzle.Progress} OF {VaultPuzzle.StepCount}";
            return false;
        }

        UnlockFirstDoor();
        return true;
    }

    public bool FinishLightPuzzle()
    {
        if (Phase != GameplayPhase.LightPuzzle || LightPuzzle is null || !LightPuzzle.Submit())
        {
            Status = "THE BEAM MISSES THE CRYSTAL";
            return false;
        }

        LightSolved = true;
        Dungeon.SetBlocked(LightGateCell, false);
        _inventory.Add("LUMEN SHARD");
        Phase = GameplayPhase.Exploring;
        Status = "LIGHT PATH OPEN  LUMEN SHARD FOUND";
        return true;
    }

    public void CheckHazards(Vector2 playerPosition, Vector2 watcherPosition)
    {
        if (Phase != GameplayPhase.Exploring)
        {
            return;
        }

        if (Dungeon.CellAt(playerPosition) == PitCell)
        {
            TriggerGameOver(GameOverReason.Pit);
            return;
        }

        if (Vector2.DistanceSquared(playerPosition, watcherPosition) < 24f * 24f)
        {
            TriggerGameOver(GameOverReason.Watcher);
        }
    }

    public bool IsNear(Vector2 playerPosition, Point cell) =>
        Vector2.DistanceSquared(playerPosition, Dungeon.CellCenter(cell)) <= InteractionRange * InteractionRange;

    private (Point Cell, InteractionTarget Target, float Distance) NearestTarget(Vector2 playerPosition)
    {
        var targets = new List<(Point Cell, InteractionTarget Target)>();
        if (!HiddenObjective) targets.Add((NpcCell, InteractionTarget.Npc));
        if (!SwitchActivated) targets.Add((SwitchCell, InteractionTarget.Switch));
        if (!DoorUnlocked) targets.Add((LockCell, InteractionTarget.Lock));
        if (DoorUnlocked && !LightSolved) targets.Add((LightCell, InteractionTarget.Light));
        targets.Add((ChestCell, InteractionTarget.Chest));

        return targets
            .Select(target => (target.Cell, target.Target,
                Distance: Vector2.DistanceSquared(playerPosition, Dungeon.CellCenter(target.Cell))))
            .Where(target => target.Distance <= InteractionRange * InteractionRange)
            .OrderBy(target => target.Distance)
            .FirstOrDefault();
    }

    private void UnlockFirstDoor()
    {
        DoorUnlocked = true;
        Dungeon.SetBlocked(DoorCell, false);
        _inventory.Add("BRASS KEY");
        Phase = GameplayPhase.Exploring;
        Status = "LOCK OPEN  BRASS KEY FOUND";
    }

    private void TriggerGameOver(GameOverReason reason)
    {
        Phase = GameplayPhase.GameOver;
        GameOverReason = reason;
        WasCaught = reason == GameOverReason.Watcher;
        Status = reason == GameOverReason.Pit ? "LOST TO THE DEPTHS" : "CAUGHT BY A WATCHER";
    }

    private enum InteractionTarget
    {
        Npc,
        Switch,
        Lock,
        Light,
        Chest,
    }
}
