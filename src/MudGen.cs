using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Game;

public sealed record GrassTuft(
    Vector2 Position,
    float Height,
    float Spread,
    float Phase,
    bool UsesLightShade);

public sealed record MudTexture(
    IReadOnlyList<Vector2> OutsideDots,
    IReadOnlyList<GrassTuft> GrassTufts);

public sealed record MudLayout(
    float Threshold,
    IReadOnlyList<AreaPolygon> Areas,
    MudTexture Texture);

/// <summary>Uses the pond's scalar field at a lower threshold, then scatters its texture details.</summary>
public sealed class MudGen
{
    public MudGen(float coverage = 0.34f)
    {
        AreaGenerator = new MudAreaGenerator(coverage);
        TextureGenerator = new MudTextureGenerator();
    }

    public MudAreaGenerator AreaGenerator { get; }
    public MudTextureGenerator TextureGenerator { get; }

    public MudLayout Generate(
        ProceduralField field,
        float pondThreshold,
        int seed,
        Func<Vector2, bool>? includes = null)
    {
        var (threshold, areas) = AreaGenerator.Generate(field, includes);
        var texture = TextureGenerator.Generate(field, threshold, pondThreshold, seed, includes);
        return new MudLayout(threshold, areas, texture);
    }
}

public sealed class MudAreaGenerator
{
    private readonly float _coverage;

    public MudAreaGenerator(float coverage)
    {
        _coverage = coverage;
    }

    public (float Threshold, IReadOnlyList<AreaPolygon> Areas) Generate(
        ProceduralField field,
        Func<Vector2, bool>? includes = null)
    {
        var threshold = field.ThresholdForCoverage(_coverage, includes);
        var areas = ContourGenerator.Generate(
            field,
            threshold,
            minimumArea: 420f,
            triangulate: true);
        return (threshold, areas);
    }
}

/// <summary>
/// Owns the animated cellular mud surface, a soft dry-mud fringe, and grass tufts above the mud.
/// </summary>
public sealed class MudTextureGenerator : IDisposable
{
    private readonly MudProceduralSurface _surface = new();
    private PerlinNoise _grassNoise = new(0);

    public Color OutlineColor { get; } = new(124, 82, 49);
    public Color OutsideDotColor { get; } = new(137, 91, 52);
    public Color GrassDarkColor { get; } = new(57, 111, 53);
    public Color GrassLightColor { get; } = new(83, 143, 66);

    public void Configure(GraphicsDevice graphicsDevice, Rectangle bounds, int seed)
    {
        _surface.Configure(graphicsDevice, bounds, seed);
        _grassNoise = new PerlinNoise(seed ^ 0x6a455);
    }

    public void Update(float totalSeconds) => _surface.Update(totalSeconds);

    public MudTexture Generate(
        ProceduralField field,
        float mudThreshold,
        float pondThreshold,
        int seed,
        Func<Vector2, bool>? includes = null)
    {
        var floorArea = field.Bounds.Width * field.Bounds.Height;
        var outsideTarget = Math.Clamp(floorArea / 4200, 40, 220);
        var grassTarget = Math.Clamp(floorArea / 4700, 40, 190);
        var outsideDots = Scatter(
            field,
            seed ^ 0x0d17d07,
            outsideTarget,
            value => value < mudThreshold && value > mudThreshold - 0.055f,
            includes);
        var grassPositions = Scatter(
            field,
            seed ^ 0x06a455,
            grassTarget,
            value => value >= mudThreshold && value < pondThreshold,
            includes,
            minimumDistance: 8f);
        var grassRandom = new Random(seed ^ 0x73a55);
        var grassTufts = new List<GrassTuft>(grassPositions.Count);
        foreach (var position in grassPositions)
        {
            grassTufts.Add(new GrassTuft(
                position,
                Height: 4.5f + (float)grassRandom.NextDouble() * 4.5f,
                Spread: 1.4f + (float)grassRandom.NextDouble() * 1.8f,
                Phase: (float)grassRandom.NextDouble() * MathHelper.TwoPi,
                UsesLightShade: grassRandom.NextDouble() > 0.52));
        }

        return new MudTexture(outsideDots, grassTufts);
    }

    public void Draw(PolygonRenderer renderer, MudLayout layout)
    {
        _surface.Draw(renderer, layout.Areas);

        foreach (var mudArea in layout.Areas)
        {
            renderer.DrawPolyline(mudArea.Outline, OutlineColor, 1.45f, closed: true);
        }

        foreach (var dot in layout.Texture.OutsideDots)
        {
            var pulse = _grassNoise.Sample(
                dot.X * 0.024f + 4.1f,
                dot.Y * 0.024f - 7.3f);
            renderer.DrawRegularPolygon(dot, 1.35f + pulse * 0.55f, 7, OutsideDotColor);
        }

        DrawGrass(renderer, layout.Texture);
    }

    public void DrawPreview(PolygonRenderer renderer, MudLayout layout)
    {
        _surface.DrawPreview(renderer);
    }

    private void DrawGrass(PolygonRenderer renderer, MudTexture texture)
    {
        var darkBlades = new List<Vector2>();
        var lightBlades = new List<Vector2>();
        foreach (var grass in texture.GrassTufts)
        {
            var blades = grass.UsesLightShade ? lightBlades : darkBlades;
            var oscillation = _grassNoise.Sample(
                grass.Position.X * 0.019f + MathF.Sin(grass.Phase) * 0.42f,
                grass.Position.Y * 0.019f + MathF.Cos(grass.Phase) * 0.42f);
            var signedOscillation = oscillation * 2f - 1f;
            var height = grass.Height * (0.94f + oscillation * 0.12f);
            var lean = signedOscillation * grass.Spread * 0.95f;

            AddBlade(blades, grass.Position, new Vector2(lean, -height));
            AddBlade(
                blades,
                grass.Position + new Vector2(-0.7f, 0.15f),
                new Vector2(-grass.Spread + lean * 0.55f, -height * 0.7f));
            AddBlade(
                blades,
                grass.Position + new Vector2(0.7f, 0.15f),
                new Vector2(grass.Spread + lean * 0.6f, -height * 0.76f));
        }

        renderer.DrawLineSegments(darkBlades, GrassDarkColor, 1.15f);
        renderer.DrawLineSegments(lightBlades, GrassLightColor, 1.05f);
    }

    private static IReadOnlyList<Vector2> Scatter(
        ProceduralField field,
        int seed,
        int targetCount,
        Func<float, bool> accepts,
        Func<Vector2, bool>? includes,
        float minimumDistance = 6f)
    {
        var random = new Random(seed);
        var points = new List<Vector2>(targetCount);
        var maximumAttempts = targetCount * 100;
        var inset = MathF.Max(field.SpacingX, field.SpacingY);

        for (var attempt = 0; attempt < maximumAttempts && points.Count < targetCount; attempt++)
        {
            var point = new Vector2(
                MathHelper.Lerp(field.Bounds.Left + inset, field.Bounds.Right - inset, (float)random.NextDouble()),
                MathHelper.Lerp(field.Bounds.Top + inset, field.Bounds.Bottom - inset, (float)random.NextDouble()));
            if ((includes is not null && !includes(point)) ||
                !accepts(field.SampleAt(point)) ||
                IsTooClose(points, point, minimumDistance))
            {
                continue;
            }

            points.Add(point);
        }

        return points;
    }

    private static bool IsTooClose(IReadOnlyList<Vector2> points, Vector2 candidate, float minimumDistance)
    {
        var minimumDistanceSquared = minimumDistance * minimumDistance;
        foreach (var point in points)
        {
            if (Vector2.DistanceSquared(point, candidate) < minimumDistanceSquared)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddBlade(ICollection<Vector2> segments, Vector2 root, Vector2 offset)
    {
        segments.Add(root);
        segments.Add(root + offset);
    }

    public void Dispose() => _surface.Dispose();
}
