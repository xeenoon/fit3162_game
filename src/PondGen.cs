using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Game;

public sealed record PondLayout(
    float Threshold,
    IReadOnlyList<AreaPolygon> Areas);

/// <summary>Owns both pond footprint generation and the pond's deliberately simple texture pass.</summary>
public sealed class PondGen
{
    public PondGen(float coverage = 0.105f)
    {
        AreaGenerator = new PondAreaGenerator(coverage);
        TextureGenerator = new PondTextureGenerator();
    }

    public PondAreaGenerator AreaGenerator { get; }
    public PondTextureGenerator TextureGenerator { get; }

    public PondLayout Generate(ProceduralField field, Func<Vector2, bool>? includes = null) =>
        AreaGenerator.Generate(field, includes);
}

public sealed class PondAreaGenerator
{
    private readonly float _coverage;

    public PondAreaGenerator(float coverage)
    {
        _coverage = coverage;
    }

    public PondLayout Generate(ProceduralField field, Func<Vector2, bool>? includes = null)
    {
        var threshold = field.ThresholdForCoverage(_coverage, includes);
        var areas = ContourGenerator.Generate(field, threshold, minimumArea: 500f);
        return new PondLayout(threshold, areas);
    }
}

/// <summary>The first pond texture is simply a flat blue fill with a readable outline.</summary>
public sealed class PondTextureGenerator
{
    public Color FillColor { get; } = new(15, 63, 92);
    public Color OutlineColor { get; } = new(52, 152, 190);

    public void Draw(PolygonRenderer renderer, PondLayout layout)
    {
        foreach (var pond in layout.Areas)
        {
            renderer.DrawTriangles(pond.FillTriangles, FillColor);
            renderer.DrawPolyline(pond.Outline, OutlineColor, 2.5f, closed: true);
        }
    }
}
