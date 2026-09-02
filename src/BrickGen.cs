using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Game;

public sealed record BrickLayer(
    Vector2[] FillTriangles,
    IReadOnlyList<Vector2[]> Outlines);

/// <summary>Builds the staggered, slightly damaged brick basis of the dungeon floor.</summary>
public sealed class BrickGen
{
    // Exactly one quarter of the original 118 x 58 prototype bricks.
    public const float BrickWidth = 29.5f;
    public const float BrickHeight = 14.5f;

    public BrickGen()
    {
        TextureGenerator = new BrickTextureGenerator();
    }

    public BrickTextureGenerator TextureGenerator { get; }

    public BrickLayer Generate(
        Rectangle bounds,
        int seed,
        Func<Vector2, bool>? includes = null,
        Vector2 offset = default)
    {
        const float mortar = 1.25f;
        var random = new Random(seed ^ 0x2f6e2b1);
        var fillTriangles = new List<Vector2>();
        var outlines = new List<Vector2[]>();
        var row = 0;

        for (var rowTop = (float)bounds.Top; rowTop < bounds.Bottom; rowTop += BrickHeight)
        {
            var stagger = row % 2 == 0 ? 0f : -BrickWidth * 0.5f;
            for (var brickLeft = bounds.Left + stagger; brickLeft < bounds.Right; brickLeft += BrickWidth)
            {
                var left = MathF.Max(brickLeft + mortar * 0.5f, bounds.Left + 2f);
                var top = rowTop + mortar * 0.5f;
                var right = MathF.Min(brickLeft + BrickWidth - mortar * 0.5f, bounds.Right - 2f);
                var bottom = MathF.Min(rowTop + BrickHeight - mortar * 0.5f, bounds.Bottom - 2f);
                if (right - left < 6f || bottom - top < 4.5f)
                {
                    continue;
                }

                var center = new Vector2((left + right) * 0.5f, (top + bottom) * 0.5f);
                var outline = CreateBrokenOutline(left, top, right, bottom, random);
                if (includes is not null && !includes(center))
                {
                    continue;
                }

                if (offset != Vector2.Zero)
                {
                    for (var index = 0; index < outline.Length; index++)
                    {
                        outline[index] += offset;
                    }
                }

                outlines.Add(outline);
                fillTriangles.AddRange(PolygonTriangulator.Triangulate(outline));
            }

            row++;
        }

        return new BrickLayer(fillTriangles.ToArray(), outlines);
    }

    private static Vector2[] CreateBrokenOutline(
        float left,
        float top,
        float right,
        float bottom,
        Random random)
    {
        var width = right - left;
        var height = bottom - top;
        var corner = 0.5f + (float)random.NextDouble() * 1.25f;
        var oppositeCorner = 0.5f + (float)random.NextDouble() * 1.25f;
        float Jitter(float strength) => ((float)random.NextDouble() * 2f - 1f) * strength;

        // Extra points along each edge keep the brick recognizable while making it look chipped.
        return
        [
            new Vector2(left + corner, top + Jitter(0.33f)),
            new Vector2(left + width * 0.38f, top + Jitter(0.53f)),
            new Vector2(left + width * 0.72f, top + Jitter(0.43f)),
            new Vector2(right - oppositeCorner, top + Jitter(0.3f)),
            new Vector2(right + Jitter(0.3f), top + oppositeCorner),
            new Vector2(right + Jitter(0.45f), top + height * 0.55f),
            new Vector2(right + Jitter(0.28f), bottom - corner),
            new Vector2(right - corner, bottom + Jitter(0.3f)),
            new Vector2(left + width * 0.61f, bottom + Jitter(0.53f)),
            new Vector2(left + width * 0.27f, bottom + Jitter(0.43f)),
            new Vector2(left + oppositeCorner, bottom + Jitter(0.28f)),
            new Vector2(left + Jitter(0.3f), bottom - oppositeCorner),
            new Vector2(left + Jitter(0.43f), top + height * 0.43f),
            new Vector2(left + corner, top + Jitter(0.28f)),
        ];
    }
}

/// <summary>The brick texture is intentionally only a flat black pass for this outline prototype.</summary>
public sealed class BrickTextureGenerator
{
    public Color FillColor { get; } = new(4, 7, 7);
    public Color OutlineColor { get; } = new(67, 76, 71);
    public Color WallFillColor { get; } = new(14, 19, 17);
    public Color WallOutlineColor { get; } = new(96, 107, 98);

    public void Draw(PolygonRenderer renderer, BrickLayer bricks, bool wallSurface = false)
    {
        renderer.DrawTriangles(bricks.FillTriangles, wallSurface ? WallFillColor : FillColor);
        renderer.DrawPolylines(
            bricks.Outlines,
            wallSurface ? WallOutlineColor : OutlineColor,
            wallSurface ? 0.9f : 0.65f,
            closed: true);
    }
}
