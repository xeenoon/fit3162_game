using System;

namespace Game;

/// <summary>
/// Deterministic CPU implementation of the stream-power erosion model described by
/// Schott, Paris, Fournier, Guérin and Galin (2023). Flow uses an Lp multiple-flow
/// direction scheme and drainage area is accumulated exactly in elevation order.
/// </summary>
public sealed class StreamPowerErosion
{
    private static readonly (int X, int Y, float Distance)[] Neighbours =
    [
        (-1, -1, 1.41421356f), (0, -1, 1f), (1, -1, 1.41421356f),
        (-1,  0, 1f),                         (1,  0, 1f),
        (-1,  1, 1.41421356f), (0,  1, 1f), (1,  1, 1.41421356f),
    ];

    public sealed record Parameters(
        int Iterations = 240,
        float TimeStep = 0.035f,
        float Erodibility = 0.075f,
        float AreaExponent = 0.8f,
        float SlopeExponent = 2f,
        float HillslopeDiffusion = 0.018f,
        float DebrisFlow = 0.12f,
        float TalusSlope = 0.58f,
        float RoutingExponent = 4f,
        float BaseLevel = 0f);

    public sealed record Result(float[] Heights, float[] DrainageArea, float[] Slopes);

    private readonly int _width;
    private readonly int _height;
    private readonly float _cellSize;
    private readonly Parameters _parameters;
    private readonly int[] _order;
    private readonly float[] _area;
    private readonly float[] _slopes;
    private readonly float[] _next;

    public StreamPowerErosion(int width, int height, float cellSize, Parameters? parameters = null)
    {
        if (width < 3 || height < 3) throw new ArgumentOutOfRangeException(nameof(width), "Grid must be at least 3x3.");
        if (!(cellSize > 0f)) throw new ArgumentOutOfRangeException(nameof(cellSize));
        _width = width;
        _height = height;
        _cellSize = cellSize;
        _parameters = parameters ?? new Parameters();
        _order = new int[width * height];
        _area = new float[width * height];
        _slopes = new float[width * height];
        _next = new float[width * height];
    }

    public Result Run(float[] heights, ReadOnlySpan<float> uplift)
        => Run(heights, uplift, ReadOnlySpan<float>.Empty);

    public Result Run(float[] heights, ReadOnlySpan<float> uplift, ReadOnlySpan<float> hardness)
    {
        if (heights.Length != _width * _height) throw new ArgumentException("Height array does not match grid dimensions.", nameof(heights));
        if (uplift.Length != heights.Length) throw new ArgumentException("Uplift array does not match height array.", nameof(uplift));
        if (!hardness.IsEmpty && hardness.Length != heights.Length) throw new ArgumentException("Hardness array does not match height array.", nameof(hardness));

        PinBoundary(heights);
        for (var iteration = 0; iteration < _parameters.Iterations; iteration++)
        {
            AccumulateDrainage(heights);
            Step(heights, uplift, hardness);
            Array.Copy(_next, heights, heights.Length);
        }
        AccumulateDrainage(heights);
        return new Result(heights, (float[])_area.Clone(), (float[])_slopes.Clone());
    }

    public Result Analyse(float[] heights)
    {
        if (heights.Length != _width * _height) throw new ArgumentException("Height array does not match grid dimensions.", nameof(heights));
        AccumulateDrainage(heights);
        return new Result(heights, (float[])_area.Clone(), (float[])_slopes.Clone());
    }

    private void AccumulateDrainage(float[] heights)
    {
        var cellArea = _cellSize * _cellSize;
        Array.Fill(_area, cellArea);
        for (var i = 0; i < _order.Length; i++) _order[i] = i;
        Array.Sort(_order, (a, b) => heights[b].CompareTo(heights[a]));

        foreach (var index in _order)
        {
            var x = index % _width;
            var y = index / _width;
            var weightSum = 0f;
            var steepest = 0f;
            foreach (var (dx, dy, distance) in Neighbours)
            {
                var nx = x + dx;
                var ny = y + dy;
                if ((uint)nx >= _width || (uint)ny >= _height) continue;
                var slope = (heights[index] - heights[ny * _width + nx]) / (_cellSize * distance);
                if (slope <= 0f) continue;
                steepest = MathF.Max(steepest, slope);
                weightSum += MathF.Pow(slope, _parameters.RoutingExponent);
            }
            _slopes[index] = steepest;
            if (weightSum <= 0f) continue;
            foreach (var (dx, dy, distance) in Neighbours)
            {
                var nx = x + dx;
                var ny = y + dy;
                if ((uint)nx >= _width || (uint)ny >= _height) continue;
                var receiver = ny * _width + nx;
                var slope = (heights[index] - heights[receiver]) / (_cellSize * distance);
                if (slope > 0f)
                    _area[receiver] += _area[index] * MathF.Pow(slope, _parameters.RoutingExponent) / weightSum;
            }
        }
    }

    private void Step(float[] heights, ReadOnlySpan<float> uplift, ReadOnlySpan<float> hardness)
    {
        var p = _parameters;
        var invCellSquared = 1f / (_cellSize * _cellSize);
        for (var y = 0; y < _height; y++)
        {
            for (var x = 0; x < _width; x++)
            {
                var index = y * _width + x;
                if (IsBoundary(x, y))
                {
                    _next[index] = p.BaseLevel;
                    continue;
                }

                var h = heights[index];
                var laplacian = (heights[index - 1] + heights[index + 1] + heights[index - _width] + heights[index + _width] - 4f * h) * invCellSquared;
                var resistance = hardness.IsEmpty ? 1f : Math.Clamp(hardness[index], 0.25f, 4f);
                var incision = p.Erodibility / resistance * MathF.Pow(_area[index], p.AreaExponent) * MathF.Pow(_slopes[index], p.SlopeExponent);
                var debris = p.DebrisFlow * MathF.Max(0f, _slopes[index] - p.TalusSlope);
                var updated = h + p.TimeStep * (uplift[index] - incision + p.HillslopeDiffusion * laplacian - debris);

                var lowestReceiver = float.PositiveInfinity;
                foreach (var (dx, dy, _) in Neighbours)
                {
                    var neighbour = heights[(y + dy) * _width + x + dx];
                    if (neighbour < h) lowestReceiver = MathF.Min(lowestReceiver, neighbour);
                }
                if (!float.IsPositiveInfinity(lowestReceiver)) updated = MathF.Max(updated, lowestReceiver);
                _next[index] = float.IsFinite(updated) ? updated : h;
            }
        }
    }

    private void PinBoundary(float[] heights)
    {
        for (var y = 0; y < _height; y++)
            for (var x = 0; x < _width; x++)
                if (IsBoundary(x, y)) heights[y * _width + x] = _parameters.BaseLevel;
    }

    private bool IsBoundary(int x, int y) => x == 0 || y == 0 || x == _width - 1 || y == _height - 1;
}
