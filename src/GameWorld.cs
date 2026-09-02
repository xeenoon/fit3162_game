using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Game;

public readonly record struct MovementInput(float Horizontal, float Vertical)
{
    public Vector2 Direction
    {
        get
        {
            var direction = new Vector2(Horizontal, Vertical);
            if (direction.LengthSquared() > 1f)
            {
                direction.Normalize();
            }

            return direction;
        }
    }
}

public sealed class PlayerController
{
    public PlayerController(Vector2 startPosition, float radius, float speed)
    {
        Position = startPosition;
        Radius = radius;
        Speed = speed;
    }

    public Vector2 Position { get; private set; }
    public float Radius { get; }
    public float Speed { get; }

    public void Reset(Vector2 position) => Position = position;

    public void Update(MovementInput input, float elapsedSeconds, DungeonMap dungeon)
    {
        if (elapsedSeconds <= 0f)
        {
            return;
        }

        var movement = input.Direction * Speed * Math.Min(elapsedSeconds, 0.1f);
        var distance = movement.Length();
        var substeps = Math.Max(1, (int)MathF.Ceiling(distance / Math.Max(1f, Radius * 0.5f)));
        var step = movement / substeps;

        for (var index = 0; index < substeps; index++)
        {
            TryMove(new Vector2(step.X, 0), dungeon);
            TryMove(new Vector2(0, step.Y), dungeon);
        }
    }

    private void TryMove(Vector2 movement, DungeonMap dungeon)
    {
        var candidate = Position + movement;
        if (dungeon.CanOccupy(candidate, Radius))
        {
            Position = candidate;
        }
    }
}

public sealed class DungeonMap
{
    private readonly string[] _cells;
    private readonly HashSet<Point> _blockedCells = [];

    public DungeonMap(
        string[] cells,
        int cellSize,
        Point origin,
        int seed = 0,
        IReadOnlyList<DungeonRoom>? rooms = null,
        IReadOnlyList<DungeonDecoration>? decorations = null)
    {
        if (cells.Length == 0 || cells[0].Length == 0)
        {
            throw new ArgumentException("A dungeon needs at least one cell.", nameof(cells));
        }

        var width = cells[0].Length;
        if (Array.Exists(cells, row => row.Length != width))
        {
            throw new ArgumentException("Every dungeon row must have the same width.", nameof(cells));
        }

        _cells = cells;
        CellSize = cellSize;
        Origin = origin;
        Seed = seed;
        Rooms = rooms ?? [];
        Decorations = decorations ?? [];
        StartCell = FindRequired('S');
        ExitCell = FindRequired('E');
    }

    public int Columns => _cells[0].Length;
    public int Rows => _cells.Length;
    public int CellSize { get; }
    public Point Origin { get; }
    public int Seed { get; }
    public IReadOnlyList<DungeonRoom> Rooms { get; }
    public IReadOnlyList<DungeonDecoration> Decorations { get; }
    public Point StartCell { get; }
    public Point ExitCell { get; }
    public Vector2 StartPosition => CellCenter(StartCell);
    public Vector2 ExitPosition => CellCenter(ExitCell);

    public bool IsBaseWalkable(int column, int row) =>
        column >= 0 && column < Columns && row >= 0 && row < Rows && _cells[row][column] != '#';

    public bool IsWalkable(int column, int row) =>
        IsBaseWalkable(column, row) && !_blockedCells.Contains(new Point(column, row));

    public void SetBlocked(Point cell, bool blocked)
    {
        if (!IsBaseWalkable(cell.X, cell.Y))
        {
            throw new ArgumentException("Only floor cells can be dynamically blocked.", nameof(cell));
        }

        if (blocked)
        {
            _blockedCells.Add(cell);
        }
        else
        {
            _blockedCells.Remove(cell);
        }
    }

    public void ClearBlockers() => _blockedCells.Clear();

    public Rectangle GetCellBounds(int column, int row) =>
        new(Origin.X + column * CellSize, Origin.Y + row * CellSize, CellSize, CellSize);

    public Vector2 CellCenter(Point cell)
    {
        var bounds = GetCellBounds(cell.X, cell.Y);
        return new Vector2(bounds.Center.X, bounds.Center.Y);
    }

    public Point CellAt(Vector2 position) => new(
        (int)MathF.Floor((position.X - Origin.X) / CellSize),
        (int)MathF.Floor((position.Y - Origin.Y) / CellSize));

    public bool CanOccupy(Vector2 center, float radius)
    {
        var first = CellAt(center - new Vector2(radius));
        var last = CellAt(center + new Vector2(radius));
        for (var row = first.Y; row <= last.Y; row++)
        {
            for (var column = first.X; column <= last.X; column++)
            {
                if (IsWalkable(column, row))
                {
                    continue;
                }

                var wall = GetCellBounds(column, row);
                var closestX = Math.Clamp(center.X, wall.Left, wall.Right);
                var closestY = Math.Clamp(center.Y, wall.Top, wall.Bottom);
                var difference = center - new Vector2(closestX, closestY);
                if (difference.LengthSquared() < radius * radius)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public bool HasRouteFromStartToExit()
    {
        var pending = new Queue<Point>();
        var visited = new HashSet<Point> { StartCell };
        pending.Enqueue(StartCell);
        Point[] directions = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (current == ExitCell)
            {
                return true;
            }

            foreach (var direction in directions)
            {
                var next = current + direction;
                if (IsWalkable(next.X, next.Y) && visited.Add(next))
                {
                    pending.Enqueue(next);
                }
            }
        }

        return false;
    }

    public string LayoutFingerprint() => string.Join('\n', _cells);

    public static DungeonMap CreateFoundationDungeon() => CreateProceduralDungeon(73419, 1);

    public static DungeonMap CreateProceduralDungeon(int seed, int variant = 1) =>
        ProceduralDungeonGenerator.Generate(seed, variant, cellSize: 36, origin: new Point(136, 92));

    private Point FindRequired(char marker)
    {
        for (var row = 0; row < Rows; row++)
        {
            var column = _cells[row].IndexOf(marker);
            if (column >= 0)
            {
                return new Point(column, row);
            }
        }

        throw new ArgumentException($"Dungeon is missing required marker '{marker}'.", nameof(_cells));
    }
}

public enum DungeonFeature
{
    Pillar,
    Stairs,
    CrackedTile,
    Pathway,
    Chest,
    Light,
    Coin,
}

public readonly record struct DungeonRoom(int X, int Y, int Width, int Height)
{
    public bool Contains(Point cell) =>
        cell.X >= X && cell.X < X + Width && cell.Y >= Y && cell.Y < Y + Height;
}

public readonly record struct DungeonDecoration(Point Cell, DungeonFeature Feature, int Variant);

public static class ProceduralDungeonGenerator
{
    private const int Columns = 28;
    private const int Rows = 15;

    public static DungeonMap Generate(int seed, int variant, int cellSize, Point origin)
    {
        var random = new Random(unchecked(seed * 486187739 + variant * 16777619));
        var cells = new char[Rows, Columns];
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                cells[row, column] = '#';
            }
        }

        CarveMaze(cells, random);
        var rooms = CarveRooms(cells, random);

        var start = new Point(1, 1);
        for (var column = 1; column <= 19; column++)
        {
            cells[1, column] = '.';
        }
        cells[2, 1] = '.';
        var exit = FindFarthestFloor(cells, start);
        cells[start.Y, start.X] = 'S';
        cells[exit.Y, exit.X] = 'E';

        var decorations = DecorateRooms(cells, rooms, start, exit, random);
        var rows = new string[Rows];
        for (var row = 0; row < Rows; row++)
        {
            var characters = new char[Columns];
            for (var column = 0; column < Columns; column++)
            {
                characters[column] = cells[row, column];
            }

            rows[row] = new string(characters);
        }

        return new DungeonMap(rows, cellSize, origin, seed, rooms, decorations);
    }

    private static void CarveMaze(char[,] cells, Random random)
    {
        var visited = new bool[Rows, Columns];
        var pending = new Stack<Point>();
        var start = new Point(1, 1);
        visited[start.Y, start.X] = true;
        cells[start.Y, start.X] = '.';
        pending.Push(start);

        Point[] directions = [new(2, 0), new(-2, 0), new(0, 2), new(0, -2)];
        while (pending.Count > 0)
        {
            var current = pending.Peek();
            Shuffle(directions, random);
            var carved = false;
            foreach (var direction in directions)
            {
                var next = current + direction;
                if (next.X <= 0 || next.X >= Columns - 1 || next.Y <= 0 || next.Y >= Rows - 1 ||
                    visited[next.Y, next.X])
                {
                    continue;
                }

                visited[next.Y, next.X] = true;
                cells[current.Y + direction.Y / 2, current.X + direction.X / 2] = '.';
                cells[next.Y, next.X] = '.';
                pending.Push(next);
                carved = true;
                break;
            }

            if (!carved)
            {
                pending.Pop();
            }
        }
    }

    private static IReadOnlyList<DungeonRoom> CarveRooms(char[,] cells, Random random)
    {
        var regions = new[]
        {
            new Rectangle(2, 2, 11, 5),
            new Rectangle(15, 2, 11, 5),
            new Rectangle(2, 8, 11, 5),
            new Rectangle(15, 8, 11, 5),
        };
        var rooms = new List<DungeonRoom>(regions.Length);

        foreach (var region in regions)
        {
            var width = random.Next(5, Math.Min(9, region.Width) + 1);
            var height = random.Next(3, Math.Min(5, region.Height) + 1);
            var x = random.Next(region.Left, region.Right - width + 1);
            var y = random.Next(region.Top, region.Bottom - height + 1);
            var room = new DungeonRoom(x, y, width, height);
            rooms.Add(room);

            for (var row = room.Y; row < room.Y + room.Height; row++)
            {
                for (var column = room.X; column < room.X + room.Width; column++)
                {
                    cells[row, column] = '.';
                }
            }
        }

        return rooms;
    }

    private static IReadOnlyList<DungeonDecoration> DecorateRooms(
        char[,] cells,
        IReadOnlyList<DungeonRoom> rooms,
        Point start,
        Point exit,
        Random random)
    {
        var candidates = new List<Point>();
        foreach (var room in rooms)
        {
            for (var row = room.Y; row < room.Y + room.Height; row++)
            {
                for (var column = room.X; column < room.X + room.Width; column++)
                {
                    var cell = new Point(column, row);
                    if (cell != start && cell != exit && cells[row, column] != '#')
                    {
                        candidates.Add(cell);
                    }
                }
            }
        }

        candidates = candidates.Distinct().ToList();
        Shuffle(candidates, random);
        var features = Enum.GetValues<DungeonFeature>();
        var targetCount = Math.Min(candidates.Count, Math.Max(features.Length, candidates.Count / 3));
        var decorations = new List<DungeonDecoration>(targetCount);
        for (var index = 0; index < targetCount; index++)
        {
            var feature = index < features.Length
                ? features[index]
                : features[random.Next(features.Length)];
            decorations.Add(new DungeonDecoration(candidates[index], feature, random.Next(4)));
        }

        return decorations;
    }

    private static Point FindFarthestFloor(char[,] cells, Point start)
    {
        var distances = new int[Rows, Columns];
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                distances[row, column] = -1;
            }
        }

        var pending = new Queue<Point>();
        pending.Enqueue(start);
        distances[start.Y, start.X] = 0;
        var farthest = start;
        Point[] directions = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (distances[current.Y, current.X] > distances[farthest.Y, farthest.X])
            {
                farthest = current;
            }

            foreach (var direction in directions)
            {
                var next = current + direction;
                if (next.X < 0 || next.X >= Columns || next.Y < 0 || next.Y >= Rows ||
                    cells[next.Y, next.X] == '#' || distances[next.Y, next.X] >= 0)
                {
                    continue;
                }

                distances[next.Y, next.X] = distances[current.Y, current.X] + 1;
                pending.Enqueue(next);
            }
        }

        return farthest;
    }

    private static void Shuffle<T>(IList<T> values, Random random)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (values[index], values[swap]) = (values[swap], values[index]);
        }
    }
}
