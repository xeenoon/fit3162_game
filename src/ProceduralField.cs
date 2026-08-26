using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Game;

/// <summary>
/// A sampled scalar field shared by every wet floor generator. Sharing this instance is what
/// guarantees that high pond areas sit inside the more generous mud threshold.
/// </summary>
public sealed class ProceduralField
{
    private const float OutsideValue = -1f;
    private readonly float[,] _values;

    private ProceduralField(Rectangle bounds, float spacingX, float spacingY, float[,] values)
    {
        Bounds = bounds;
        SpacingX = spacingX;
        SpacingY = spacingY;
        _values = values;
    }

    public Rectangle Bounds { get; }
    public int Columns => _values.GetLength(1);
    public int Rows => _values.GetLength(0);
    public float SpacingX { get; }
    public float SpacingY { get; }

    public float this[int column, int row] => _values[row, column];

    public static ProceduralField Generate(Rectangle bounds, int seed, float targetSpacing = 14f)
    {
        var columns = Math.Max(8, (int)MathF.Ceiling(bounds.Width / targetSpacing) + 1);
        var rows = Math.Max(8, (int)MathF.Ceiling(bounds.Height / targetSpacing) + 1);
        var spacingX = bounds.Width / (float)(columns - 1);
        var spacingY = bounds.Height / (float)(rows - 1);
        var values = new float[rows, columns];
        var noise = new PerlinNoise(seed);

        // Offsets keep nearby integer seeds from merely looking translated.
        var random = new Random(seed ^ 0x5f3759df);
        var offsetX = (float)random.NextDouble() * 800f;
        var offsetY = (float)random.NextDouble() * 800f;

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if (row == 0 || column == 0 || row == rows - 1 || column == columns - 1)
                {
                    // A low padded border forces every marching-squares contour to close.
                    values[row, column] = OutsideValue;
                    continue;
                }

                var worldX = bounds.Left + column * spacingX;
                var worldY = bounds.Top + row * spacingY;
                const float broadScale = 0.0062f;
                var broad = noise.Fractal(
                    (worldX + offsetX) * broadScale,
                    (worldY + offsetY) * broadScale,
                    octaves: 4);
                var detail = noise.Fractal(
                    (worldX - offsetY * 0.37f) * broadScale * 2.3f,
                    (worldY + offsetX * 0.41f) * broadScale * 2.3f,
                    octaves: 2,
                    persistence: 0.45f);

                values[row, column] = broad * 0.82f + detail * 0.18f;
            }
        }

        return new ProceduralField(bounds, spacingX, spacingY, values);
    }

    /// <summary>Returns a value threshold that fills approximately the requested floor fraction.</summary>
    public float ThresholdForCoverage(float coverage, Func<Vector2, bool>? includes = null)
    {
        coverage = MathHelper.Clamp(coverage, 0.01f, 0.95f);
        var samples = new List<float>((Columns - 2) * (Rows - 2));
        for (var row = 1; row < Rows - 1; row++)
        {
            for (var column = 1; column < Columns - 1; column++)
            {
                if (includes is null || includes(PositionOf(column, row)))
                {
                    samples.Add(_values[row, column]);
                }
            }
        }

        if (samples.Count == 0)
        {
            return 1f;
        }

        samples.Sort();
        var index = (int)MathF.Round((1f - coverage) * (samples.Count - 1));
        return samples[Math.Clamp(index, 0, samples.Count - 1)];
    }

    public Vector2 PositionOf(int column, int row) => new(
        Bounds.Left + column * SpacingX,
        Bounds.Top + row * SpacingY);

    public float SampleAt(Vector2 position)
    {
        var gridX = (position.X - Bounds.Left) / SpacingX;
        var gridY = (position.Y - Bounds.Top) / SpacingY;
        var left = Math.Clamp((int)MathF.Floor(gridX), 0, Columns - 2);
        var top = Math.Clamp((int)MathF.Floor(gridY), 0, Rows - 2);
        var amountX = MathHelper.Clamp(gridX - left, 0f, 1f);
        var amountY = MathHelper.Clamp(gridY - top, 0f, 1f);
        var upper = MathHelper.Lerp(_values[top, left], _values[top, left + 1], amountX);
        var lower = MathHelper.Lerp(_values[top + 1, left], _values[top + 1, left + 1], amountX);
        return MathHelper.Lerp(upper, lower, amountY);
    }
}
