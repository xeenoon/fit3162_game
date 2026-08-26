using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Game;

public readonly record struct WorldSeed(int Value)
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct Rgb(byte R, byte G, byte B);

public sealed record TerrainSettings(
    float Size,
    int GridResolution,
    float MinHeight,
    float MaxHeight);

public sealed record ScatterDefinition(
    int TargetCount,
    float MinimumSpacing,
    float MinimumSlopeDegrees,
    float MaximumSlopeDegrees,
    float MinimumNormalizedHeight,
    float MaximumNormalizedHeight);

/// <summary>
/// Stable data describing the lowland side of the generated world. Rendering code
/// consumes this palette and these placement rules but never owns them.
/// </summary>
public sealed record HillsBiomeDefinition(
    Rgb LowGrass,
    Rgb HillGrass,
    Rgb RidgeGrass,
    Rgb SteepEarth,
    Rgb LakeWater,
    float BroadNoiseScale,
    float MediumNoiseScale,
    float ContinentNoiseScale,
    float DetailNoiseScale,
    ScatterDefinition Trees,
    ScatterDefinition Shrubs,
    ScatterDefinition Rocks)
{
    public static HillsBiomeDefinition Default { get; } = new(
        LowGrass: new Rgb(76, 86, 68),
        HillGrass: new Rgb(96, 106, 78),
        RidgeGrass: new Rgb(136, 124, 91),
        SteepEarth: new Rgb(93, 86, 75),
        LakeWater: new Rgb(78, 101, 108),
        BroadNoiseScale: 0.024f,
        MediumNoiseScale: 0.071f,
        ContinentNoiseScale: 0.0105f,
        DetailNoiseScale: 0.17f,
        Trees: new ScatterDefinition(76, 3.35f, 0f, 14f, 0.05f, 0.70f),
        Shrubs: new ScatterDefinition(250, 1.20f, 0f, 18f, 0.02f, 0.78f),
        Rocks: new ScatterDefinition(70, 2.85f, 5f, 29f, 0.22f, 0.82f));
}

public sealed record MountainBiomeDefinition(
    Rgb BareRock,
    Rgb VegetatedRock,
    Rgb Snow,
    Rgb VegetatedSnow,
    float TransitionScale,
    float FoldScale,
    float SnowLineHeight,
    ScatterDefinition Pines,
    ScatterDefinition CrookedPines,
    ScatterDefinition AlpineShrubs,
    ScatterDefinition LichenRocks,
    ScatterDefinition DeadConifers)
{
    public static MountainBiomeDefinition Default { get; } = new(
        BareRock: new Rgb(105, 106, 103),
        VegetatedRock: new Rgb(82, 91, 76),
        Snow: new Rgb(218, 222, 221),
        VegetatedSnow: new Rgb(139, 148, 136),
        TransitionScale: 0.021f,
        FoldScale: 0.052f,
        SnowLineHeight: 23.0f,
        Pines: new ScatterDefinition(96, 2.65f, 0f, 27f, 0.30f, 0.78f),
        CrookedPines: new ScatterDefinition(32, 1.2f, 0f, 55f, 0.30f, 0.86f),
        AlpineShrubs: new ScatterDefinition(125, 1.55f, 0f, 55f, 0.30f, 0.91f),
        LichenRocks: new ScatterDefinition(68, 2.4f, 4f, 40f, 0.42f, 1f),
        DeadConifers: new ScatterDefinition(16, 4.4f, 0f, 29f, 0.35f, 0.86f));
}

public enum MountainElevationBand
{
    None,
    LowerSlope,
    MidSlope,
    Treeline,
    Alpine,
    Peak,
}

public enum TerrainSurface
{
    LowGrass,
    HillGrass,
    RidgeGrass,
    SteepEarth,
    MountainRock,
    MountainVegetatedRock,
    MountainSnow,
    MountainVegetatedSnow,
}

public enum PropType
{
    Tree,
    Shrub,
    Rock,
    PineTree,
    CrookedPine,
    AlpineShrub,
    LichenRock,
    DeadConifer,
    DeadTree,
    FallenLog,
    Stump,
}

public enum TerrainFeatureType
{
    WornPath,
}

public sealed record DungeonSite(
    Vector3 Position,
    int DungeonId,
    string Name,
    float OrientationRadians,
    float ClearedAreaRadius);

public sealed record PropPlacement(
    PropType Type,
    Vector3 Position,
    float RotationRadians,
    Vector3 Scale,
    float ColorVariation,
    Vector3 SurfaceNormal,
    float LeanRadians,
    float LeanDirectionRadians);

public sealed record TerrainFeature(
    TerrainFeatureType Type,
    IReadOnlyList<Vector3> Points,
    float Width,
    float ColorVariation);

public sealed record LakeDefinition(
    Vector2 Center,
    Vector2 Radius,
    float WaterHeight,
    IReadOnlyList<Vector2> Shoreline);

public enum MountainFormationType
{
    CliffFace,
    Ravine,
    Drainage,
    ScreeFan,
    HillRidge,
    HillDrainage,
}

public sealed record MountainFormation(
    MountainFormationType Type,
    IReadOnlyList<Vector2> Points,
    float Width,
    float Strength);

/// <summary>A regular sampled heightfield. Its arrays contain generated values, never GPU objects.</summary>
public sealed class GeneratedTerrain
{
    private readonly float[] _heights;
    private readonly float[] _mountainInfluences;
    private readonly MountainElevationBand[] _mountainBands;
    private readonly Vector3[] _normals;
    private readonly TerrainSurface[] _surfaces;
    private readonly float[] _distanceToRidge;
    private readonly float[] _distanceToHillRidge;
    private readonly float[] _distanceToDrainage;
    private readonly float[] _distanceToHillDrainage;
    private readonly float[] _flowAccumulation;
    private readonly float[] _sunExposure;
    private readonly float[] _rockMasks;
    private readonly float[] _screeDeposits;
    private readonly float[] _alluvialDeposits;
    private readonly float[] _screeMasks;
    private readonly float[] _snowMasks;
    private readonly float[] _forestMasks;
    private readonly float[] _grassMasks;
    private readonly float[] _strataMasks;
    private readonly float[] _clearingMasks;
    private readonly float[] _wetValleyMasks;
    private readonly float[] _hillOutcropMasks;
    private readonly float[] _ambientAccessibility;
    private readonly float[] _sunVisibility;

    internal GeneratedTerrain(TerrainSettings settings, float[] heights, float[] mountainInfluences)
    {
        Settings = settings;
        _heights = heights;
        _mountainInfluences = mountainInfluences;
        _mountainBands = new MountainElevationBand[heights.Length];
        _normals = new Vector3[heights.Length];
        _surfaces = new TerrainSurface[heights.Length];
        _distanceToRidge = new float[heights.Length];
        _distanceToHillRidge = new float[heights.Length];
        _distanceToDrainage = Enumerable.Repeat(float.MaxValue, heights.Length).ToArray();
        _distanceToHillDrainage = Enumerable.Repeat(float.MaxValue, heights.Length).ToArray();
        _flowAccumulation = new float[heights.Length];
        _sunExposure = new float[heights.Length];
        _rockMasks = new float[heights.Length];
        _screeDeposits = new float[heights.Length];
        _alluvialDeposits = new float[heights.Length];
        _screeMasks = new float[heights.Length];
        _snowMasks = new float[heights.Length];
        _forestMasks = new float[heights.Length];
        _grassMasks = new float[heights.Length];
        _strataMasks = new float[heights.Length];
        _clearingMasks = new float[heights.Length];
        _wetValleyMasks = new float[heights.Length];
        _hillOutcropMasks = new float[heights.Length];
        _ambientAccessibility = Enumerable.Repeat(1f, heights.Length).ToArray();
        _sunVisibility = Enumerable.Repeat(1f, heights.Length).ToArray();
    }

    public TerrainSettings Settings { get; }
    public IReadOnlyList<float> Heights => _heights;
    public IReadOnlyList<float> MountainInfluences => _mountainInfluences;
    public IReadOnlyList<MountainElevationBand> MountainBands => _mountainBands;
    public IReadOnlyList<Vector3> Normals => _normals;
    public IReadOnlyList<TerrainSurface> Surfaces => _surfaces;
    public IReadOnlyList<float> DistancesToRidge => _distanceToRidge;
    public IReadOnlyList<float> DistancesToHillRidge => _distanceToHillRidge;
    public IReadOnlyList<float> DistancesToDrainage => _distanceToDrainage;
    public IReadOnlyList<float> DistancesToHillDrainage => _distanceToHillDrainage;
    public IReadOnlyList<float> FlowAccumulations => _flowAccumulation;
    public IReadOnlyList<float> SunExposures => _sunExposure;
    public IReadOnlyList<float> RockMasks => _rockMasks;
    public IReadOnlyList<float> ScreeDeposits => _screeDeposits;
    public IReadOnlyList<float> AlluvialDeposits => _alluvialDeposits;
    public IReadOnlyList<float> ScreeMasks => _screeMasks;
    public IReadOnlyList<float> SnowMasks => _snowMasks;
    public IReadOnlyList<float> ForestMasks => _forestMasks;
    public IReadOnlyList<float> GrassMasks => _grassMasks;
    public IReadOnlyList<float> StrataMasks => _strataMasks;
    public IReadOnlyList<float> ClearingMasks => _clearingMasks;
    public IReadOnlyList<float> WetValleyMasks => _wetValleyMasks;
    public IReadOnlyList<float> HillOutcropMasks => _hillOutcropMasks;
    public IReadOnlyList<float> AmbientAccessibility => _ambientAccessibility;
    public IReadOnlyList<float> SunVisibility => _sunVisibility;
    public float GridSpacing => Settings.Size / (Settings.GridResolution - 1);

    public int IndexOf(int column, int row) => row * Settings.GridResolution + column;
    public float HeightAt(int column, int row) => _heights[IndexOf(column, row)];
    public Vector3 NormalAt(int column, int row) => _normals[IndexOf(column, row)];
    public TerrainSurface SurfaceAt(int column, int row) => _surfaces[IndexOf(column, row)];
    public float MountainInfluenceAt(int column, int row) => _mountainInfluences[IndexOf(column, row)];
    public MountainElevationBand MountainBandAt(int column, int row) => _mountainBands[IndexOf(column, row)];
    public float DistanceToRidgeAt(int column, int row) => _distanceToRidge[IndexOf(column, row)];
    public float DistanceToHillRidgeAt(int column, int row) => _distanceToHillRidge[IndexOf(column, row)];
    public float DistanceToDrainageAt(int column, int row) => _distanceToDrainage[IndexOf(column, row)];
    public float DistanceToHillDrainageAt(int column, int row) => _distanceToHillDrainage[IndexOf(column, row)];
    public float FlowAccumulationAt(int column, int row) => _flowAccumulation[IndexOf(column, row)];
    public float RockMaskAt(int column, int row) => _rockMasks[IndexOf(column, row)];
    public float ScreeDepositAt(int column, int row) => _screeDeposits[IndexOf(column, row)];
    public float AlluvialDepositAt(int column, int row) => _alluvialDeposits[IndexOf(column, row)];
    public float ScreeMaskAt(int column, int row) => _screeMasks[IndexOf(column, row)];
    public float SnowMaskAt(int column, int row) => _snowMasks[IndexOf(column, row)];
    public float ForestMaskAt(int column, int row) => _forestMasks[IndexOf(column, row)];
    public float GrassMaskAt(int column, int row) => _grassMasks[IndexOf(column, row)];
    public float StrataMaskAt(int column, int row) => _strataMasks[IndexOf(column, row)];
    public float ClearingMaskAt(int column, int row) => _clearingMasks[IndexOf(column, row)];
    public float WetValleyMaskAt(int column, int row) => _wetValleyMasks[IndexOf(column, row)];
    public float HillOutcropMaskAt(int column, int row) => _hillOutcropMasks[IndexOf(column, row)];
    public float AmbientAccessibilityAt(int column, int row) => _ambientAccessibility[IndexOf(column, row)];
    public float SunVisibilityAt(int column, int row) => _sunVisibility[IndexOf(column, row)];

    public Vector3 PositionAt(int column, int row) => new(
        -Settings.Size * 0.5f + column * GridSpacing,
        HeightAt(column, row),
        -Settings.Size * 0.5f + row * GridSpacing);

    public float SampleHeight(float worldX, float worldZ)
    {
        GridCoordinates(worldX, worldZ, out var left, out var top, out var amountX, out var amountZ);
        var upper = MathHelper.Lerp(HeightAt(left, top), HeightAt(left + 1, top), amountX);
        var lower = MathHelper.Lerp(HeightAt(left, top + 1), HeightAt(left + 1, top + 1), amountX);
        return MathHelper.Lerp(upper, lower, amountZ);
    }

    public Vector3 SampleNormal(float worldX, float worldZ)
    {
        GridCoordinates(worldX, worldZ, out var left, out var top, out var amountX, out var amountZ);
        var upper = Vector3.Lerp(NormalAt(left, top), NormalAt(left + 1, top), amountX);
        var lower = Vector3.Lerp(NormalAt(left, top + 1), NormalAt(left + 1, top + 1), amountX);
        var normal = Vector3.Lerp(upper, lower, amountZ);
        normal.Normalize();
        return normal;
    }

    public float SampleMountainInfluence(float worldX, float worldZ)
    {
        GridCoordinates(worldX, worldZ, out var left, out var top, out var amountX, out var amountZ);
        var upper = MathHelper.Lerp(MountainInfluenceAt(left, top), MountainInfluenceAt(left + 1, top), amountX);
        var lower = MathHelper.Lerp(MountainInfluenceAt(left, top + 1), MountainInfluenceAt(left + 1, top + 1), amountX);
        return MathHelper.Lerp(upper, lower, amountZ);
    }

    public TerrainSurface SampleSurface(float worldX, float worldZ)
    {
        GridCoordinates(worldX, worldZ, out var left, out var top, out var amountX, out var amountZ);
        return SurfaceAt(left + (amountX >= 0.5f ? 1 : 0), top + (amountZ >= 0.5f ? 1 : 0));
    }

    public MountainElevationBand SampleMountainBand(float worldX, float worldZ)
    {
        GridCoordinates(worldX, worldZ, out var left, out var top, out var amountX, out var amountZ);
        return MountainBandAt(left + (amountX >= 0.5f ? 1 : 0), top + (amountZ >= 0.5f ? 1 : 0));
    }

    public float SampleDistanceToRidge(float worldX, float worldZ) => SampleField(_distanceToRidge, worldX, worldZ);
    public float SampleDistanceToHillRidge(float worldX, float worldZ) => SampleField(_distanceToHillRidge, worldX, worldZ);
    public float SampleDistanceToDrainage(float worldX, float worldZ) => SampleField(_distanceToDrainage, worldX, worldZ);
    public float SampleDistanceToHillDrainage(float worldX, float worldZ) => SampleField(_distanceToHillDrainage, worldX, worldZ);
    public float SampleFlowAccumulation(float worldX, float worldZ) => SampleField(_flowAccumulation, worldX, worldZ);
    public float SampleRockMask(float worldX, float worldZ) => SampleField(_rockMasks, worldX, worldZ);
    public float SampleScreeDeposit(float worldX, float worldZ) => SampleField(_screeDeposits, worldX, worldZ);
    public float SampleAlluvialDeposit(float worldX, float worldZ) => SampleField(_alluvialDeposits, worldX, worldZ);
    public float SampleScreeMask(float worldX, float worldZ) => SampleField(_screeMasks, worldX, worldZ);
    public float SampleSnowMask(float worldX, float worldZ) => SampleField(_snowMasks, worldX, worldZ);
    public float SampleForestMask(float worldX, float worldZ) => SampleField(_forestMasks, worldX, worldZ);
    public float SampleGrassMask(float worldX, float worldZ) => SampleField(_grassMasks, worldX, worldZ);
    public float SampleStrataMask(float worldX, float worldZ) => SampleField(_strataMasks, worldX, worldZ);
    public float SampleClearingMask(float worldX, float worldZ) => SampleField(_clearingMasks, worldX, worldZ);
    public float SampleWetValleyMask(float worldX, float worldZ) => SampleField(_wetValleyMasks, worldX, worldZ);
    public float SampleHillOutcropMask(float worldX, float worldZ) => SampleField(_hillOutcropMasks, worldX, worldZ);
    public float SampleAmbientAccessibility(float worldX, float worldZ) => SampleField(_ambientAccessibility, worldX, worldZ);
    public float SampleSunVisibility(float worldX, float worldZ) => SampleField(_sunVisibility, worldX, worldZ);

    public float SampleCurvature(float worldX, float worldZ)
    {
        var spacing = GridSpacing;
        var center = SampleHeight(worldX, worldZ);
        var half = Settings.Size * 0.5f;
        var left = SampleHeight(MathHelper.Clamp(worldX - spacing, -half, half), worldZ);
        var right = SampleHeight(MathHelper.Clamp(worldX + spacing, -half, half), worldZ);
        var near = SampleHeight(worldX, MathHelper.Clamp(worldZ - spacing, -half, half));
        var far = SampleHeight(worldX, MathHelper.Clamp(worldZ + spacing, -half, half));
        return (left + right + near + far - 4f * center) / (spacing * spacing);
    }

    public float NormalizedHeight(float height) => MathHelper.Clamp(
        (height - Settings.MinHeight) / (Settings.MaxHeight - Settings.MinHeight), 0f, 1f);

    internal void SetHeight(int column, int row, float height) => _heights[IndexOf(column, row)] = height;
    internal void SetDerived(
        int column,
        int row,
        Vector3 normal,
        TerrainSurface surface,
        MountainElevationBand mountainBand)
    {
        var index = IndexOf(column, row);
        _normals[index] = normal;
        _surfaces[index] = surface;
        _mountainBands[index] = mountainBand;
    }

    internal void SetAnalysis(
        int column,
        int row,
        float distanceToRidge,
        float distanceToDrainage,
        float flowAccumulation,
        float sunExposure,
        float rockMask,
        float screeMask,
        float snowMask,
        float forestMask,
        float grassMask,
        float strataMask)
    {
        var index = IndexOf(column, row);
        _distanceToRidge[index] = distanceToRidge;
        _distanceToDrainage[index] = distanceToDrainage;
        _flowAccumulation[index] = flowAccumulation;
        _sunExposure[index] = sunExposure;
        _rockMasks[index] = rockMask;
        _screeMasks[index] = screeMask;
        _snowMasks[index] = snowMask;
        _forestMasks[index] = forestMask;
        _grassMasks[index] = grassMask;
        _strataMasks[index] = strataMask;
    }

    internal void SetHydrology(int column, int row, float distanceToDrainage, float flowAccumulation)
    {
        var index = IndexOf(column, row);
        _distanceToDrainage[index] = distanceToDrainage;
        _flowAccumulation[index] = flowAccumulation;
    }

    internal void AddScreeDeposit(int column, int row, float strength)
    {
        var index = IndexOf(column, row);
        _screeDeposits[index] = Math.Max(_screeDeposits[index], MathHelper.Clamp(strength, 0f, 1f));
    }

    internal void SetAlluvialDeposit(int column, int row, float strength) =>
        _alluvialDeposits[IndexOf(column, row)] = MathHelper.Clamp(strength, 0f, 1f);

    internal void SetHillAnalysis(
        int column,
        int row,
        float distanceToHillRidge,
        float distanceToHillDrainage,
        float clearingMask,
        float wetValleyMask,
        float hillOutcropMask)
    {
        var index = IndexOf(column, row);
        _distanceToHillRidge[index] = distanceToHillRidge;
        _distanceToHillDrainage[index] = distanceToHillDrainage;
        _clearingMasks[index] = clearingMask;
        _wetValleyMasks[index] = wetValleyMask;
        _hillOutcropMasks[index] = hillOutcropMask;
    }

    internal void SetHillHydrology(int column, int row, float distanceToHillDrainage)
    {
        _distanceToHillDrainage[IndexOf(column, row)] = distanceToHillDrainage;
    }

    internal void SetAmbientAccessibility(int column, int row, float accessibility)
    {
        _ambientAccessibility[IndexOf(column, row)] = MathHelper.Clamp(accessibility, 0f, 1f);
    }

    internal void SetSunVisibility(int column, int row, float visibility)
    {
        _sunVisibility[IndexOf(column, row)] = MathHelper.Clamp(visibility, 0f, 1f);
    }

    internal void SetMasks(
        int column,
        int row,
        float sunExposure,
        float rockMask,
        float screeMask,
        float snowMask,
        float forestMask,
        float grassMask,
        float strataMask)
    {
        var index = IndexOf(column, row);
        _sunExposure[index] = sunExposure;
        _rockMasks[index] = rockMask;
        _screeMasks[index] = screeMask;
        _snowMasks[index] = snowMask;
        _forestMasks[index] = forestMask;
        _grassMasks[index] = grassMask;
        _strataMasks[index] = strataMask;
    }

    private float SampleField(float[] field, float worldX, float worldZ)
    {
        GridCoordinates(worldX, worldZ, out var left, out var top, out var amountX, out var amountZ);
        var upper = MathHelper.Lerp(field[IndexOf(left, top)], field[IndexOf(left + 1, top)], amountX);
        var lower = MathHelper.Lerp(field[IndexOf(left, top + 1)], field[IndexOf(left + 1, top + 1)], amountX);
        return MathHelper.Lerp(upper, lower, amountZ);
    }

    private void GridCoordinates(
        float worldX,
        float worldZ,
        out int left,
        out int top,
        out float amountX,
        out float amountZ)
    {
        var gridX = (worldX + Settings.Size * 0.5f) / GridSpacing;
        var gridZ = (worldZ + Settings.Size * 0.5f) / GridSpacing;
        left = Math.Clamp((int)MathF.Floor(gridX), 0, Settings.GridResolution - 2);
        top = Math.Clamp((int)MathF.Floor(gridZ), 0, Settings.GridResolution - 2);
        amountX = MathHelper.Clamp(gridX - left, 0f, 1f);
        amountZ = MathHelper.Clamp(gridZ - top, 0f, 1f);
    }
}

internal sealed class StableRandom
{
    private ulong _state;

    public StableRandom(ulong seed) => _state = seed;

    public ulong NextUInt64()
    {
        _state += 0x9e3779b97f4a7c15UL;
        var value = _state;
        value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
        value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
        return value ^ (value >> 31);
    }

    public float NextFloat() => (NextUInt64() >> 40) * (1f / 16_777_216f);
    public float Range(float minimum, float maximum) => MathHelper.Lerp(minimum, maximum, NextFloat());
    public int Next(int maximumExclusive) => (int)(NextUInt64() % (uint)maximumExclusive);
}

/// <summary>Perlin noise with a runtime-independent, explicitly seeded permutation.</summary>
internal sealed class StablePerlinNoise
{
    private readonly int[] _permutation = new int[512];

    public StablePerlinNoise(ulong seed)
    {
        var values = Enumerable.Range(0, 256).ToArray();
        var random = new StableRandom(seed);
        for (var index = values.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (values[index], values[swap]) = (values[swap], values[index]);
        }

        for (var index = 0; index < _permutation.Length; index++)
        {
            _permutation[index] = values[index & 255];
        }
    }

    public float Sample(float x, float y)
    {
        var floorX = MathF.Floor(x);
        var floorY = MathF.Floor(y);
        var cellX = (int)floorX & 255;
        var cellY = (int)floorY & 255;
        var localX = x - floorX;
        var localY = y - floorY;
        var fadeX = Fade(localX);
        var fadeY = Fade(localY);

        var topLeft = _permutation[_permutation[cellX] + cellY];
        var topRight = _permutation[_permutation[cellX + 1] + cellY];
        var bottomLeft = _permutation[_permutation[cellX] + cellY + 1];
        var bottomRight = _permutation[_permutation[cellX + 1] + cellY + 1];
        var top = MathHelper.Lerp(Gradient(topLeft, localX, localY), Gradient(topRight, localX - 1f, localY), fadeX);
        var bottom = MathHelper.Lerp(Gradient(bottomLeft, localX, localY - 1f), Gradient(bottomRight, localX - 1f, localY - 1f), fadeX);
        return MathHelper.Lerp(top, bottom, fadeY) * 0.5f + 0.5f;
    }

    public float Fractal(float x, float y, int octaves, float persistence)
    {
        var value = 0f;
        var amplitude = 1f;
        var totalAmplitude = 0f;
        var frequency = 1f;
        for (var octave = 0; octave < octaves; octave++)
        {
            value += Sample(x * frequency, y * frequency) * amplitude;
            totalAmplitude += amplitude;
            amplitude *= persistence;
            frequency *= 2f;
        }

        return value / totalAmplitude;
    }

    private static float Fade(float value) => value * value * value * (value * (value * 6f - 15f) + 10f);
    private static float Gradient(int hash, float x, float y) => (hash & 7) switch
    {
        0 => x + y,
        1 => -x + y,
        2 => x - y,
        3 => -x - y,
        4 => x,
        5 => -x,
        6 => y,
        _ => -y,
    };
}

internal static class HillsWorldGeneration
{
    private const float SiteClearRadius = 5.2f;

    public static GeneratedWorld Generate(
        int seed,
        HillsBiomeDefinition biome,
        MountainBiomeDefinition mountains,
        float worldScale = 1f)
    {
        if (worldScale is not (1f or 4f or 16f))
        {
            throw new ArgumentOutOfRangeException(nameof(worldScale), "World scale must be 1, 4, or 16.");
        }

        var worldSeed = new WorldSeed(seed);
        var settings = new TerrainSettings(
            Size: 140f * worldScale,
            // The close map needs enough lateral samples for narrow drainage ribs to remain
            // smooth in profile. Larger overviews keep their existing bounded resolutions.
            GridResolution: worldScale switch { 4f => 193, 16f => 241, _ => 193 },
            MinHeight: -0.5f,
            MaxHeight: 27.5f);
        var terrain = GenerateTerrain(worldSeed, settings, biome, mountains, out var lakes, out var mountainFormations);
        var dungeons = new List<DungeonChoice>(WorldGenerator.DungeonCount);
        for (var index = 0; index < WorldGenerator.DungeonCount; index++)
        {
            var dungeonBiome = index == WorldGenerator.DungeonCount - 1 ? Biome.Mountain : Biome.Hills;
            dungeons.Add(new DungeonChoice(index + 1, WorldGenerator.DungeonNames[index], dungeonBiome, index + 1));
        }

        var sites = SelectDungeonSites(worldSeed, terrain, dungeons, lakes);
        BlendSiteClearings(terrain, sites);
        RecalculateTerrain(worldSeed, terrain, mountains);
        CalculateAmbientAccessibility(terrain);
        sites = sites.Select(site => site with
        {
            Position = new Vector3(site.Position.X, terrain.SampleHeight(site.Position.X, site.Position.Z), site.Position.Z),
        }).ToArray();

        var features = GeneratePaths(worldSeed, terrain, sites);
        var props = new List<PropPlacement>();
        var trees = Scatter(worldSeed, "trees", PropType.Tree, biome.Trees, terrain, sites, features, lakes, []);
        props.AddRange(trees);
        props.AddRange(Scatter(worldSeed, "shrubs", PropType.Shrub, biome.Shrubs, terrain, sites, features, lakes, trees));
        props.AddRange(Scatter(worldSeed, "rocks", PropType.Rock, biome.Rocks, terrain, sites, features, lakes, []));
        var pines = ScatterMountain(
            worldSeed,
            "mountain-pines",
            PropType.PineTree,
            mountains.Pines,
            terrain,
            sites,
            features,
            lakes,
            [],
            MountainElevationBand.LowerSlope,
            MountainElevationBand.MidSlope);
        props.AddRange(pines);
        var crookedPines = ScatterMountain(
            worldSeed,
            "mountain-crooked-pines",
            PropType.CrookedPine,
            mountains.CrookedPines,
            terrain,
            sites,
            features,
            lakes,
            pines,
            MountainElevationBand.MidSlope,
            MountainElevationBand.Treeline);
        props.AddRange(crookedPines);
        var mountainTrees = pines.Concat(crookedPines).ToArray();
        props.AddRange(ScatterMountain(
            worldSeed,
            "mountain-shrubs",
            PropType.AlpineShrub,
            mountains.AlpineShrubs,
            terrain,
            sites,
            features,
            lakes,
            mountainTrees,
            MountainElevationBand.Treeline,
            MountainElevationBand.Alpine));
        props.AddRange(ScatterMountain(
            worldSeed,
            "mountain-lichen-rocks",
            PropType.LichenRock,
            mountains.LichenRocks,
            terrain,
            sites,
            features,
            lakes,
            [],
            MountainElevationBand.MidSlope,
            MountainElevationBand.Treeline,
            MountainElevationBand.Alpine,
            MountainElevationBand.Peak));
        props.AddRange(ScatterMountain(
            worldSeed,
            "mountain-dead-conifers",
            PropType.DeadConifer,
            mountains.DeadConifers,
            terrain,
            sites,
            features,
            lakes,
            mountainTrees,
            MountainElevationBand.LowerSlope,
            MountainElevationBand.MidSlope,
            MountainElevationBand.Treeline));
        props.AddRange(ScatterDeadwood(worldSeed, terrain, sites, features, lakes, trees));
        return new GeneratedWorld(
            worldSeed,
            settings,
            biome,
            mountains,
            terrain,
            dungeons,
            sites,
            props,
            features,
            lakes,
            mountainFormations);
    }

    private static GeneratedTerrain GenerateTerrain(
        WorldSeed seed,
        TerrainSettings settings,
        HillsBiomeDefinition biome,
        MountainBiomeDefinition mountains,
        out LakeDefinition[] lakes,
        out MountainFormation[] mountainFormations)
    {
        var random = new StableRandom(DeriveSeed(seed, "terrain-shape"));
        var broadNoise = new StablePerlinNoise(DeriveSeed(seed, "terrain-broad"));
        var mediumNoise = new StablePerlinNoise(DeriveSeed(seed, "terrain-medium"));
        var ridgeNoise = new StablePerlinNoise(DeriveSeed(seed, "terrain-ridges"));
        var continentNoise = new StablePerlinNoise(DeriveSeed(seed, "terrain-continent"));
        var detailNoise = new StablePerlinNoise(DeriveSeed(seed, "terrain-detail"));
        var broadOffset = new Vector2(random.Range(-700f, 700f), random.Range(-700f, 700f));
        var continentOffset = new Vector2(random.Range(-900f, 900f), random.Range(-900f, 900f));
        var mountainRandom = new StableRandom(DeriveSeed(seed, "mountain-silhouette"));
        var mountainWarp = new StablePerlinNoise(DeriveSeed(seed, "mountain-ridge-warp"));
        var hillRandom = new StableRandom(DeriveSeed(seed, "hill-ridge-graph"));
        var hillWarp = new StablePerlinNoise(DeriveSeed(seed, "hill-domain-warp"));
        var hills = GenerateMacroHills(random, settings.Size);
        var ridgeGraph = GenerateRidgelineGraph(mountainRandom, settings.Size);
        var hillRidgeGraph = GenerateHillRidgelineGraph(hillRandom, settings.Size);

        var heights = new float[settings.GridResolution * settings.GridResolution];
        var mountainInfluences = new float[heights.Length];
        var uplift = new float[heights.Length];
        var hardness = new float[heights.Length];
        var strataNoise = new StablePerlinNoise(DeriveSeed(seed, "bedrock-hardness"));
        var terrain = new GeneratedTerrain(settings, heights, mountainInfluences);
        for (var row = 0; row < settings.GridResolution; row++)
        {
            for (var column = 0; column < settings.GridResolution; column++)
            {
                var x = -settings.Size * 0.5f + column * terrain.GridSpacing;
                var z = -settings.Size * 0.5f + row * terrain.GridSpacing;
                var broad = broadNoise.Fractal(
                    (x + broadOffset.X) * biome.BroadNoiseScale,
                    (z + broadOffset.Y) * biome.BroadNoiseScale,
                    octaves: 4,
                    persistence: 0.51f);
                var medium = mediumNoise.Fractal(
                    (x - broadOffset.Y * 0.31f) * biome.MediumNoiseScale,
                    (z + broadOffset.X * 0.27f) * biome.MediumNoiseScale,
                    octaves: 3,
                    persistence: 0.46f);
                var ridgeSample = ridgeNoise.Fractal(
                    (x + broadOffset.Y * 0.17f) * biome.MediumNoiseScale * 0.78f,
                    (z - broadOffset.X * 0.19f) * biome.MediumNoiseScale * 0.78f,
                    octaves: 3,
                    persistence: 0.48f);
                var ridge = 1f - MathF.Abs(ridgeSample * 2f - 1f);
                var continent = continentNoise.Fractal(
                    (x + continentOffset.X) * biome.ContinentNoiseScale,
                    (z + continentOffset.Y) * biome.ContinentNoiseScale,
                    octaves: 2,
                    persistence: 0.55f);
                var detail = detailNoise.Fractal(
                    (x - broadOffset.X) * biome.DetailNoiseScale,
                    (z + broadOffset.Y) * biome.DetailNoiseScale,
                    octaves: 2,
                    persistence: 0.42f);

                var hillMass = 0f;
                foreach (var hill in hills)
                {
                    var distanceSquared = Vector2.DistanceSquared(new Vector2(x, z), hill.Center);
                    hillMass += MathF.Exp(-distanceSquared / (2f * hill.Radius * hill.Radius)) * hill.Amplitude;
                }

                var distanceFromCenter = MathF.Sqrt(x * x + z * z) / (settings.Size * 0.71f);
                var edgeFalloff = SmoothStep(0.66f, 1f, distanceFromCenter) * 0.16f;
                var normalized = 0.18f + hillMass +
                                 (broad - 0.5f) * 0.50f +
                                 (medium - 0.5f) * 0.16f +
                                 (ridge - 0.62f) * 0.105f +
                                 (continent - 0.5f) * 0.24f +
                                 (detail - 0.5f) * 0.028f - edgeFalloff;
                normalized = MathHelper.Clamp(normalized, 0f, 1f);

                var position = new Vector2(x, z);
                var ridgeContribution = RidgeContribution(position, ridgeGraph, out var distanceToRidge);
                var broadMountainBase = BroadRidgeBase(position, ridgeGraph);
                var mountainInfluence = SmoothStep(
                    settings.Size * 0.18f,
                    settings.Size * 0.025f,
                    distanceToRidge);
                if (settings.Size <= 140.01f)
                {
                    var lakeExclusion = Math.Min(
                        Vector2.Distance(position, new Vector2(43f, 29f)),
                        Math.Min(
                            Vector2.Distance(position, new Vector2(-45f, 15f)),
                            Vector2.Distance(position, new Vector2(22f, 11f))));
                    mountainInfluence *= SmoothStep(12f, 24f, lakeExclusion);
                    ridgeContribution *= mountainInfluence;
                    broadMountainBase *= mountainInfluence;
                }
                var hillsHeight = MathHelper.Lerp(-0.5f, 14.5f, normalized);
                var hillRidgeHeight = HillRidgeContribution(position, hillRidgeGraph, out var distanceToHillRidge);
                var warpX = (hillWarp.Fractal(x * 0.018f, z * 0.018f, 2, 0.54f) - 0.5f) * 10f;
                var warpZ = (hillWarp.Fractal(x * 0.018f + 37f, z * 0.018f - 19f, 2, 0.54f) - 0.5f) * 10f;
                var warpedDetail = (mediumNoise.Fractal((x + warpX) * 0.048f, (z + warpZ) * 0.048f, 3, 0.48f) - 0.5f) * 1.35f;
                var mediumRock = (mountainWarp.Fractal(x * 0.062f, z * 0.062f, 3, 0.47f) - 0.5f) *
                                 2.6f * mountainInfluence;
                // Broad terrain remains the lowlands; deliberate ridge geometry carries the
                // mountain silhouette, with noise restricted to surface distortion.
                var formedHeight = (hillsHeight + hillRidgeHeight + warpedDetail) * (1f - mountainInfluence * 0.18f) +
                                   broadMountainBase * 0.22f +
                                   ridgeContribution * 0.68f +
                                   mediumRock * 0.08f;
                if (!float.IsFinite(formedHeight))
                {
                    throw new InvalidOperationException(
                        $"Invalid terrain height at seed {seed}, ({x}, {z}): ridge={ridgeContribution}, base={broadMountainBase}.");
                }
                var index = terrain.IndexOf(column, row);
                mountainInfluences[index] = mountainInfluence;
                // Preserve the authored layout as tectonic forcing: the ridge graph raises
                // mountains, macro hills sustain rolling relief, and the edge remains an outlet.
                var outletFade = 1f - SmoothStep(0.58f, 0.98f, distanceFromCenter);
                uplift[index] = (0.0025f + hillMass * 0.010f + mountainInfluence * 0.026f) * outletFade;
                var foldedStrata = 0.5f + 0.5f * MathF.Sin((x * 0.11f + z * 0.055f) +
                    (strataNoise.Fractal(x * 0.018f, z * 0.018f, 3, 0.52f) - 0.5f) * 4.2f);
                hardness[index] = MathHelper.Lerp(0.58f, 1.85f, foldedStrata);
                terrain.SetAnalysis(column, row, distanceToRidge, float.MaxValue, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f);
                terrain.SetHillAnalysis(column, row, distanceToHillRidge, float.MaxValue, 0f, 0f, 0f);
                terrain.SetHeight(column, row, MathHelper.Clamp(formedHeight, settings.MinHeight, settings.MaxHeight));
            }
        }

        FillPits(terrain);
        var erosion = new StreamPowerErosion(
            settings.GridResolution,
            settings.GridResolution,
            terrain.GridSpacing,
            new StreamPowerErosion.Parameters(
                Iterations: 160,
                TimeStep: 0.035f,
                Erodibility: 0.00012f,
                HillslopeDiffusion: 0.012f,
                DebrisFlow: 0.08f,
                TalusSlope: 0.62f,
                BaseLevel: settings.MinHeight));
        var eroded = erosion.Run(heights, uplift, hardness);
        for (var index = 0; index < heights.Length; index++)
            heights[index] = MathHelper.Clamp(eroded.Heights[index], settings.MinHeight, settings.MaxHeight);

        var flow = CalculateDrainageFlow(terrain);
        var drainageLines = TraceDrainageLines(terrain, flow, ridgeGraph, desiredCount: 12);
        var hillDrainageLines = TraceHillDrainageLines(terrain, flow, hillRidgeGraph, desiredCount: 9);
        SetErodedHydrology(terrain, eroded.DrainageArea, eroded.Slopes, drainageLines, hillDrainageLines);
        ScreeFan[] screeFans = [];
        mountainFormations = BuildPipelineFormations(ridgeGraph, drainageLines, screeFans, hillRidgeGraph, hillDrainageLines);
        lakes = CarveLakes(seed, terrain);
        RecalculateTerrain(seed, terrain, mountains);
        CalculateAmbientAccessibility(terrain);
        return terrain;
    }

    private static void SetErodedHydrology(
        GeneratedTerrain terrain,
        IReadOnlyList<float> drainageArea,
        IReadOnlyList<float> slopes,
        IReadOnlyList<Vector2[]> mountainDrainage,
        IReadOnlyList<Vector2[]> hillDrainage)
    {
        for (var row = 0; row < terrain.Settings.GridResolution; row++)
        {
            for (var column = 0; column < terrain.Settings.GridResolution; column++)
            {
                var position3 = terrain.PositionAt(column, row);
                var position = new Vector2(position3.X, position3.Z);
                var mountainDistance = mountainDrainage.Count == 0
                    ? float.MaxValue
                    : mountainDrainage.Min(line => DistanceToPolyline(position, line, out _));
                var hillDistance = hillDrainage.Count == 0
                    ? float.MaxValue
                    : hillDrainage.Min(line => DistanceToPolyline(position, line, out _));
                terrain.SetHydrology(column, row, mountainDistance, drainageArea[terrain.IndexOf(column, row)]);
                terrain.SetHillHydrology(column, row, hillDistance);
                var talus = SmoothStep(0.34f, 0.64f, slopes[terrain.IndexOf(column, row)]);
                if (talus > 0f) terrain.AddScreeDeposit(column, row, talus);
                var flowDeposit = SmoothStep(18f, 520f, drainageArea[terrain.IndexOf(column, row)]) *
                                  (1f - SmoothStep(0.06f, 0.24f, slopes[terrain.IndexOf(column, row)]));
                terrain.SetAlluvialDeposit(column, row, flowDeposit);
                if (flowDeposit > 0f)
                    terrain.SetHeight(column, row, Math.Min(terrain.Settings.MaxHeight,
                        terrain.HeightAt(column, row) + flowDeposit * 0.10f));
            }
        }
    }

    private static void CalculateAmbientAccessibility(GeneratedTerrain terrain)
    {
        var resolution = terrain.Settings.GridResolution;
        var gridSpacing = terrain.GridSpacing;
        ReadOnlySpan<(int X, int Z)> directions =
        [
            (1, 0), (-1, 0), (0, 1), (0, -1),
            (1, 1), (-1, 1), (1, -1), (-1, -1),
        ];

        for (var row = 0; row < resolution; row++)
        {
            for (var column = 0; column < resolution; column++)
            {
                var origin = terrain.HeightAt(column, row);
                var horizonOcclusion = 0f;
                foreach (var direction in directions)
                {
                    var strongestHorizon = 0f;
                    for (var step = 1; step <= 10; step++)
                    {
                        var sampleColumn = column + direction.X * step;
                        var sampleRow = row + direction.Z * step;
                        if (sampleColumn < 0 || sampleColumn >= resolution || sampleRow < 0 || sampleRow >= resolution)
                        {
                            break;
                        }

                        var run = gridSpacing * step * (direction.X != 0 && direction.Z != 0 ? 1.41421356f : 1f);
                        var rise = terrain.HeightAt(sampleColumn, sampleRow) - origin;
                        strongestHorizon = Math.Max(strongestHorizon, MathF.Atan2(rise, run));
                    }

                    horizonOcclusion += SmoothStep(0.02f, 0.72f, strongestHorizon);
                }

                var horizonAverage = horizonOcclusion / directions.Length;
                var gully = 1f - SmoothStep(0.8f, 5.8f, Math.Min(
                    terrain.DistanceToDrainageAt(column, row),
                    terrain.DistanceToHillDrainageAt(column, row)));
                var cliffContact = SmoothStep(0.25f, 0.82f, terrain.ScreeDepositAt(column, row));
                var accessibility = 1f - horizonAverage * 0.54f - gully * 0.12f - cliffContact * 0.10f;
                terrain.SetAmbientAccessibility(column, row, MathHelper.Clamp(accessibility, 0.36f, 1f));

                // March toward the sun. The visibility ramp is deliberately broad so the CPU
                // heightfield behaves like a soft distance shadow rather than a hard pixel mask.
                var sunToSurface = -TerrainLighting.SunDirection;
                var horizontal = new Vector2(sunToSurface.X, sunToSurface.Z);
                horizontal.Normalize();
                var elevationSlope = sunToSurface.Y / Math.Max(0.0001f,
                    MathF.Sqrt(sunToSurface.X * sunToSurface.X + sunToSurface.Z * sunToSurface.Z));
                var strongestBlocker = float.NegativeInfinity;
                for (var step = 1; step <= 28; step++)
                {
                    var sampleColumn = (int)MathF.Round(column + horizontal.X * step);
                    var sampleRow = (int)MathF.Round(row + horizontal.Y * step);
                    if (sampleColumn < 0 || sampleColumn >= resolution || sampleRow < 0 || sampleRow >= resolution)
                    {
                        break;
                    }

                    var distance = gridSpacing * step;
                    var rayHeight = origin + distance * elevationSlope;
                    strongestBlocker = Math.Max(strongestBlocker, terrain.HeightAt(sampleColumn, sampleRow) - rayHeight);
                }

                var visibility = 1f - SmoothStep(-0.35f, 1.25f, strongestBlocker);
                terrain.SetSunVisibility(column, row, MathHelper.Lerp(0.12f, 1f, visibility));
            }
        }
    }

    private static RidgeLine[] GenerateRidgelineGraph(StableRandom random, float terrainSize)
    {
        var half = terrainSize * 0.5f;
        var scale = terrainSize / 140f;
        var main = JitteredRidge(
            new Vector2(-half * 0.86f, -half * 0.54f),
            new Vector2(half * 0.86f, -half * 0.42f),
            11,
            random,
            terrainSize * 0.055f,
            22.5f,
            24f * MathF.Sqrt(scale),
            2.7f);
        var branchStartA = main.Points[3];
        var branchStartB = main.Points[7];
        var branchA = JitteredRidge(
            branchStartA,
            new Vector2(-half * 0.28f, -half * 0.08f),
            7,
            random,
            terrainSize * 0.042f,
            17f,
            23f * MathF.Sqrt(scale),
            2.35f);
        var branchB = JitteredRidge(
            branchStartB,
            new Vector2(half * 0.42f, -half * 0.04f),
            7,
            random,
            terrainSize * 0.044f,
            18f,
            24f * MathF.Sqrt(scale),
            2.4f);
        return [main, branchA, branchB];
    }

    private static RidgeLine[] GenerateHillRidgelineGraph(StableRandom random, float terrainSize)
    {
        var half = terrainSize * 0.5f;
        var scale = MathF.Sqrt(terrainSize / 140f);
        var main = JitteredRidge(
            new Vector2(-half * 0.86f, half * 0.70f),
            new Vector2(half * 0.78f, half * 0.10f),
            12, random, terrainSize * 0.075f, 4.7f, 29f * scale, 1.18f);
        var branchA = JitteredRidge(
            main.Points[3], new Vector2(-half * 0.76f, -half * 0.30f),
            8, random, terrainSize * 0.062f, 3.5f, 25f * scale, 1.12f);
        var branchB = JitteredRidge(
            main.Points[6], new Vector2(half * 0.10f, half * 0.84f),
            7, random, terrainSize * 0.060f, 3.2f, 24f * scale, 1.10f);
        var branchC = JitteredRidge(
            main.Points[9], new Vector2(half * 0.72f, -half * 0.62f),
            8, random, terrainSize * 0.065f, 3.8f, 26f * scale, 1.14f);
        return [main, branchA, branchB, branchC];
    }

    private static float HillRidgeContribution(Vector2 position, IReadOnlyList<RidgeLine> ridges, out float nearestDistance)
    {
        nearestDistance = float.MaxValue;
        var contribution = 0f;
        foreach (var ridge in ridges)
        {
            var distance = DistanceToPolyline(position, ridge.Points, out _);
            nearestDistance = Math.Min(nearestDistance, distance);
            var rounded = SmoothStep(ridge.Width, 0f, distance);
            contribution = Math.Max(contribution, ridge.CrestHeight * rounded);
        }
        return contribution;
    }

    private static RidgeLine JitteredRidge(
        Vector2 start,
        Vector2 end,
        int pointCount,
        StableRandom random,
        float jitter,
        float crestHeight,
        float width,
        float sharpness)
    {
        var direction = Vector2.Normalize(end - start);
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var points = new Vector2[pointCount];
        var wandering = 0f;
        for (var index = 0; index < pointCount; index++)
        {
            var amount = index / (float)(pointCount - 1);
            wandering = MathHelper.Clamp(wandering + random.Range(-jitter * 0.34f, jitter * 0.34f), -jitter, jitter);
            var endpointFade = MathF.Sin(amount * MathHelper.Pi);
            points[index] = Vector2.Lerp(start, end, amount) + perpendicular * wandering * endpointFade;
        }

        points[0] = start;
        points[^1] = end;
        return new RidgeLine(points, crestHeight, width, sharpness, random.Range(0f, MathHelper.TwoPi));
    }

    private static float RidgeContribution(Vector2 position, IReadOnlyList<RidgeLine> ridges, out float nearestDistance)
    {
        nearestDistance = float.MaxValue;
        var contribution = 0f;
        foreach (var ridge in ridges)
        {
            var distance = DistanceToPolyline(position, ridge.Points, out _, out var ridgeProgress);
            nearestDistance = Math.Min(nearestDistance, distance);
            var normalized = MathHelper.Clamp(1f - distance / ridge.Width, 0f, 1f);
            var sharpCrest = MathF.Pow(normalized, ridge.Sharpness);
            var shoulder = MathF.Pow(normalized, 0.88f);
            var endpointTaper = SmoothStep(0f, 0.10f, ridgeProgress) * SmoothStep(1f, 0.90f, ridgeProgress);
            var crestProfile = 0.76f + MathF.Sin(ridgeProgress * MathHelper.Pi * 3f + ridge.CrestPhase) * 0.13f +
                               MathF.Sin(ridgeProgress * MathHelper.Pi * 7f + ridge.CrestPhase * 0.43f) * 0.07f;
            contribution = Math.Max(contribution,
                ridge.CrestHeight * crestProfile * MathHelper.Lerp(0.62f, 1f, endpointTaper) *
                (sharpCrest * 0.58f + shoulder * 0.42f));
        }

        return contribution;
    }

    private static float BroadRidgeBase(Vector2 position, IReadOnlyList<RidgeLine> ridges)
    {
        var baseHeight = 0f;
        foreach (var ridge in ridges)
        {
            var distance = DistanceToPolyline(position, ridge.Points, out _);
            var normalized = MathHelper.Clamp(1f - distance / (ridge.Width * 2.5f), 0f, 1f);
            baseHeight = Math.Max(baseHeight, ridge.CrestHeight * MathF.Pow(normalized, 1.65f));
        }

        return baseHeight;
    }

    private static void FillPits(GeneratedTerrain terrain)
    {
        var resolution = terrain.Settings.GridResolution;
        for (var pass = 0; pass < 5; pass++)
        {
            for (var row = 1; row < resolution - 1; row++)
            {
                for (var column = 1; column < resolution - 1; column++)
                {
                    var minimumNeighbour = float.MaxValue;
                    for (var offsetRow = -1; offsetRow <= 1; offsetRow++)
                    {
                        for (var offsetColumn = -1; offsetColumn <= 1; offsetColumn++)
                        {
                            if (offsetColumn == 0 && offsetRow == 0) continue;
                            minimumNeighbour = Math.Min(minimumNeighbour, terrain.HeightAt(column + offsetColumn, row + offsetRow));
                        }
                    }

                    if (terrain.HeightAt(column, row) < minimumNeighbour)
                    {
                        terrain.SetHeight(column, row, Math.Min(minimumNeighbour + 0.002f, terrain.Settings.MaxHeight));
                    }
                }
            }
        }
    }

    private static DrainageFlow CalculateDrainageFlow(GeneratedTerrain terrain)
    {
        var resolution = terrain.Settings.GridResolution;
        var downhill = Enumerable.Repeat(-1, resolution * resolution).ToArray();
        var accumulation = Enumerable.Repeat(1f, downhill.Length).ToArray();
        for (var row = 0; row < resolution; row++)
        {
            for (var column = 0; column < resolution; column++)
            {
                var index = terrain.IndexOf(column, row);
                var bestHeight = terrain.HeightAt(column, row);
                for (var offsetRow = -1; offsetRow <= 1; offsetRow++)
                {
                    for (var offsetColumn = -1; offsetColumn <= 1; offsetColumn++)
                    {
                        if (offsetColumn == 0 && offsetRow == 0) continue;
                        var nextColumn = column + offsetColumn;
                        var nextRow = row + offsetRow;
                        if (nextColumn < 0 || nextColumn >= resolution || nextRow < 0 || nextRow >= resolution) continue;
                        var candidate = terrain.HeightAt(nextColumn, nextRow);
                        if (candidate < bestHeight - 0.0001f)
                        {
                            bestHeight = candidate;
                            downhill[index] = terrain.IndexOf(nextColumn, nextRow);
                        }
                    }
                }
            }
        }

        foreach (var index in Enumerable.Range(0, downhill.Length).OrderByDescending(index => terrain.Heights[index]))
        {
            if (downhill[index] >= 0)
            {
                accumulation[downhill[index]] += accumulation[index];
            }
        }

        return new DrainageFlow(downhill, accumulation);
    }

    private static Vector2[][] TraceDrainageLines(
        GeneratedTerrain terrain,
        DrainageFlow flow,
        IReadOnlyList<RidgeLine> ridges,
        int desiredCount)
    {
        var resolution = terrain.Settings.GridResolution;
        var lines = new List<Vector2[]>(desiredCount);
        var mainRidge = ridges[0];
        const int primaryCount = 8;
        for (var primary = 0; primary < primaryCount && lines.Count < desiredCount; primary++)
        {
            var amount = MathHelper.Lerp(0.07f, 0.93f, primary / (float)(primaryCount - 1));
            var crest = PointOnPolyline(mainRidge.Points, amount, out var tangent);
            var line = TraceFaceDrainage(terrain, flow, crest, tangent, primary * 0.91f);
            if (line.Length >= 9) lines.Add(line);
        }

        // Short high tributaries join alternating primary channels. The shared lower sections
        // produce the repeated Y-shaped drainage visible on naturally eroded range faces.
        int[] branchParents = [1, 3, 5, 6];
        foreach (var parentIndex in branchParents)
        {
            if (lines.Count >= desiredCount || parentIndex >= lines.Count) break;
            var parent = lines[parentIndex];
            var parentAmount = MathHelper.Lerp(0.07f, 0.93f, parentIndex / (float)(primaryCount - 1));
            var branchAmount = MathHelper.Clamp(parentAmount + (parentIndex % 2 == 0 ? -0.035f : 0.035f), 0.03f, 0.97f);
            var crest = PointOnPolyline(mainRidge.Points, branchAmount, out _);
            var joinIndex = Math.Clamp(parent.Length / 3, 3, parent.Length - 3);
            var branch = new List<Vector2>();
            for (var step = 0; step <= 6; step++)
            {
                var t = step / 6f;
                var bend = new Vector2(MathF.Sin(t * MathHelper.Pi) * (parentIndex % 2 == 0 ? -1.8f : 1.8f), 0f);
                branch.Add(Vector2.Lerp(crest, parent[joinIndex], t) + bend);
            }
            branch.AddRange(parent.Skip(joinIndex + 1));
            lines.Add(SmoothPolyline(branch, passes: 2));
        }

        return lines.Take(desiredCount).ToArray();
    }

    private static Vector2[] TraceFaceDrainage(
        GeneratedTerrain terrain,
        DrainageFlow flow,
        Vector2 crest,
        Vector2 tangent,
        float phase)
    {
        var outward = new Vector2(-tangent.Y, tangent.X);
        if (outward.Y < 0f) outward = -outward;
        var current = crest + outward * terrain.GridSpacing * 1.4f;
        var points = new List<Vector2> { crest, current };
        var resolution = terrain.Settings.GridResolution;
        const int maximumSteps = 34;
        for (var step = 0; step < maximumSteps; step++)
        {
            var meander = MathF.Sin(step * 0.47f + phase) * 0.20f;
            var desired = Vector2.Normalize(outward + tangent * meander);
            var best = current + desired * 1.45f;
            var bestScore = float.MaxValue;
            foreach (var angle in new[] { -0.48f, -0.24f, 0f, 0.24f, 0.48f })
            {
                var cosine = MathF.Cos(angle);
                var sine = MathF.Sin(angle);
                var direction = new Vector2(desired.X * cosine - desired.Y * sine, desired.X * sine + desired.Y * cosine);
                var candidate = current + direction * 1.45f;
                var height = terrain.SampleHeight(candidate.X, candidate.Y);
                var column = Math.Clamp((int)MathF.Round((candidate.X + terrain.Settings.Size * 0.5f) / terrain.GridSpacing), 0, resolution - 1);
                var row = Math.Clamp((int)MathF.Round((candidate.Y + terrain.Settings.Size * 0.5f) / terrain.GridSpacing), 0, resolution - 1);
                var flowBonus = MathF.Log2(flow.Accumulation[terrain.IndexOf(column, row)] + 1f) * 0.12f;
                var score = height - flowBonus + MathF.Abs(angle) * 0.16f;
                if (score >= bestScore) continue;
                bestScore = score;
                best = candidate;
            }

            current = best;
            points.Add(current);
            if (points.Count > 10 && terrain.SampleMountainInfluence(current.X, current.Y) < 0.16f) break;
        }

        return SmoothPolyline(points, passes: 2);
    }

    private static Vector2 PointOnPolyline(IReadOnlyList<Vector2> points, float amount, out Vector2 tangent)
    {
        var lengths = Enumerable.Range(0, points.Count - 1)
            .Select(index => Vector2.Distance(points[index], points[index + 1]))
            .ToArray();
        var target = lengths.Sum() * MathHelper.Clamp(amount, 0f, 1f);
        var traversed = 0f;
        for (var index = 0; index < lengths.Length; index++)
        {
            if (traversed + lengths[index] < target)
            {
                traversed += lengths[index];
                continue;
            }
            tangent = Vector2.Normalize(points[index + 1] - points[index]);
            return Vector2.Lerp(points[index], points[index + 1], (target - traversed) / Math.Max(lengths[index], 0.0001f));
        }
        tangent = Vector2.Normalize(points[^1] - points[^2]);
        return points[^1];
    }

    private static Vector2[][] TraceHillDrainageLines(
        GeneratedTerrain terrain,
        DrainageFlow flow,
        IReadOnlyList<RidgeLine> hillRidges,
        int desiredCount)
    {
        var resolution = terrain.Settings.GridResolution;
        var candidates = Enumerable.Range(0, flow.Accumulation.Length)
            .Where(index => terrain.MountainInfluences[index] < 0.24f &&
                            terrain.DistanceToHillRidgeAt(index % resolution, index / resolution) < 7f)
            .OrderByDescending(index => flow.Accumulation[index] * 0.65f + terrain.Heights[index] * 0.35f)
            .ToArray();
        var lines = new List<Vector2[]>(desiredCount);
        foreach (var source in candidates)
        {
            var sourcePosition = terrain.PositionAt(source % resolution, source / resolution);
            var source2 = new Vector2(sourcePosition.X, sourcePosition.Z);
            if (lines.Any(line => Vector2.Distance(line[0], source2) < 12f)) continue;
            var points = new List<Vector2>();
            var current = source;
            var visited = new HashSet<int>();
            while (current >= 0 && visited.Add(current) && points.Count < 48)
            {
                var column = current % resolution;
                var row = current / resolution;
                var position = terrain.PositionAt(column, row);
                points.Add(new Vector2(position.X, position.Z));
                if (terrain.MountainInfluenceAt(column, row) > 0.28f) break;
                current = flow.Downhill[current];
            }
            if (points.Count < 10) continue;
            lines.Add(SmoothPolyline(points.Where((_, index) => index % 2 == 0).Append(points[^1]).ToArray(), passes: 3));
            if (lines.Count >= desiredCount) break;
        }
        return lines.ToArray();
    }

    private static void CarveHillDrainageErosion(GeneratedTerrain terrain, IReadOnlyList<Vector2[]> lines)
    {
        for (var row = 0; row < terrain.Settings.GridResolution; row++)
        {
            for (var column = 0; column < terrain.Settings.GridResolution; column++)
            {
                if (terrain.MountainInfluenceAt(column, row) > 0.30f) continue;
                var position3 = terrain.PositionAt(column, row);
                var position = new Vector2(position3.X, position3.Z);
                var closest = float.MaxValue;
                var erosion = 0f;
                foreach (var line in lines)
                {
                    var distance = DistanceToPolyline(position, line, out _, out var progress);
                    closest = Math.Min(closest, distance);
                    var width = MathHelper.Lerp(1.6f, 4.8f, progress);
                    var channel = MathF.Pow(MathHelper.Clamp(1f - distance / width, 0f, 1f), 1.6f);
                    erosion = Math.Max(erosion, channel * MathHelper.Lerp(0.18f, 0.78f, progress));
                }
                terrain.SetHeight(column, row, Math.Max(terrain.Settings.MinHeight, position3.Y - erosion));
                terrain.SetHillHydrology(column, row, closest);
            }
        }
    }

    private static void CarveDrainageErosion(
        GeneratedTerrain terrain,
        DrainageFlow flow,
        IReadOnlyList<Vector2[]> drainageLines)
    {
        for (var row = 0; row < terrain.Settings.GridResolution; row++)
        {
            for (var column = 0; column < terrain.Settings.GridResolution; column++)
            {
                var position3 = terrain.PositionAt(column, row);
                var position = new Vector2(position3.X, position3.Z);
                var erosion = 0f;
                var closest = float.MaxValue;
                foreach (var line in drainageLines)
                {
                    var distance = DistanceToPolyline(position, line, out _, out var progress);
                    closest = Math.Min(closest, distance);
                    var sourceTaper = SmoothStep(0f, 0.16f, progress);
                    var downstream = SmoothStep(0.05f, 0.92f, progress);
                    var width = MathHelper.Lerp(0.55f, 4.25f, downstream) * sourceTaper;
                    var vChannel = MathF.Pow(MathHelper.Clamp(1f - distance / Math.Max(0.35f, width), 0f, 1f), 1.38f);
                    var depth = MathHelper.Lerp(0.20f, 4.45f, downstream) * sourceTaper;
                    erosion = Math.Max(erosion, vChannel * depth);
                }
                var localFlow = flow.Accumulation[terrain.IndexOf(column, row)];
                erosion *= terrain.MountainInfluenceAt(column, row);
                terrain.SetHeight(column, row, MathHelper.Clamp(position3.Y - erosion, terrain.Settings.MinHeight, terrain.Settings.MaxHeight));
                terrain.SetHydrology(column, row, closest, localFlow);
            }
        }
    }

    private static Vector2[] SmoothPolyline(IReadOnlyList<Vector2> points, int passes)
    {
        var smoothed = points.ToArray();
        for (var pass = 0; pass < passes; pass++)
        {
            var next = smoothed.ToArray();
            for (var index = 1; index < smoothed.Length - 1; index++)
            {
                next[index] = smoothed[index - 1] * 0.22f + smoothed[index] * 0.56f + smoothed[index + 1] * 0.22f;
            }
            smoothed = next;
        }
        return smoothed;
    }

    private static ScreeFan[] DepositScreeFans(GeneratedTerrain terrain, IReadOnlyList<RidgeLine> ridges, int count)
    {
        var main = ridges[0];
        var fans = new ScreeFan[count];
        for (var fanIndex = 0; fanIndex < count; fanIndex++)
        {
            var ridgePoint = main.Points[fanIndex == 0 ? 3 : 7];
            var next = main.Points[fanIndex == 0 ? 4 : 8];
            var ridgeDirection = Vector2.Normalize(next - ridgePoint);
            var downhill = new Vector2(-ridgeDirection.Y, ridgeDirection.X) * (fanIndex == 0 ? 1f : -1f);
            var length = 18f;
            var points = new Vector2[9];
            for (var index = 0; index < points.Length; index++)
            {
                points[index] = ridgePoint + downhill * (index / (float)(points.Length - 1) * length);
            }
            fans[fanIndex] = new ScreeFan(points, 7f);

            for (var row = 0; row < terrain.Settings.GridResolution; row++)
            {
                for (var column = 0; column < terrain.Settings.GridResolution; column++)
                {
                    var position = terrain.PositionAt(column, row);
                    var relative = new Vector2(position.X, position.Z) - ridgePoint;
                    var along = Vector2.Dot(relative, downhill);
                    if (along < 0f || along > length) continue;
                    var across = MathF.Abs(Vector2.Dot(relative, ridgeDirection));
                    var fanWidth = MathHelper.Lerp(0.8f, fans[fanIndex].Width, along / length);
                    var blend = MathF.Pow(MathHelper.Clamp(1f - across / fanWidth, 0f, 1f), 2f);
                    if (blend > 0f)
                    {
                        var smoothed = NeighbourMean(terrain, column, row);
                        terrain.SetHeight(column, row, MathHelper.Lerp(position.Y, smoothed - 0.08f, blend * 0.28f));
                        // Preserve the exact widening deposit footprint for biome analysis and
                        // rendering; talus is not inferred as a uniform band around the range.
                        terrain.AddScreeDeposit(column, row, blend * SmoothStep(0.04f, 0.98f, along / length));
                    }
                }
            }
        }
        return fans;
    }

    private static float NeighbourMean(GeneratedTerrain terrain, int column, int row)
    {
        var resolution = terrain.Settings.GridResolution;
        var total = 0f;
        var samples = 0;
        for (var dz = -1; dz <= 1; dz++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var x = Math.Clamp(column + dx, 0, resolution - 1);
                var z = Math.Clamp(row + dz, 0, resolution - 1);
                total += terrain.HeightAt(x, z);
                samples++;
            }
        }
        return total / samples;
    }

    private static MountainFormation[] BuildPipelineFormations(
        IReadOnlyList<RidgeLine> ridges,
        IReadOnlyList<Vector2[]> drainageLines,
        IReadOnlyList<ScreeFan> screeFans,
        IReadOnlyList<RidgeLine> hillRidges,
        IReadOnlyList<Vector2[]> hillDrainageLines)
    {
        var formations = new List<MountainFormation>();
        formations.AddRange(ridges.Select(ridge => new MountainFormation(MountainFormationType.CliffFace, ridge.Points, ridge.Width * 0.18f, ridge.CrestHeight * 0.10f)));
        formations.AddRange(drainageLines.Select(line => new MountainFormation(MountainFormationType.Drainage, line, 1.45f, 1.6f)));
        formations.AddRange(screeFans.Select(fan => new MountainFormation(MountainFormationType.ScreeFan, fan.Points, fan.Width, 1f)));
        formations.AddRange(hillRidges.Select(ridge => new MountainFormation(MountainFormationType.HillRidge, ridge.Points, ridge.Width, ridge.CrestHeight)));
        formations.AddRange(hillDrainageLines.Select(line => new MountainFormation(MountainFormationType.HillDrainage, line, 3.4f, 0.72f)));
        return formations.ToArray();
    }

    private static MacroHill[] GenerateMacroHills(StableRandom random, float terrainSize)
    {
        if (terrainSize <= 140.01f)
        {
            return
            [
                new(new Vector2(random.Range(-48f, -38f), random.Range(25f, 35f)), random.Range(25f, 33f), random.Range(0.16f, 0.24f)),
                new(new Vector2(random.Range(-18f, -7f), random.Range(10f, 21f)), random.Range(28f, 37f), random.Range(0.18f, 0.27f)),
                new(new Vector2(random.Range(18f, 30f), random.Range(13f, 24f)), random.Range(26f, 35f), random.Range(0.17f, 0.25f)),
                new(new Vector2(random.Range(-35f, -22f), random.Range(-14f, -4f)), random.Range(25f, 34f), random.Range(0.16f, 0.24f)),
                new(new Vector2(random.Range(34f, 48f), random.Range(-25f, -14f)), random.Range(27f, 36f), random.Range(0.18f, 0.27f)),
            ];
        }

        var tileCount = (int)MathF.Ceiling(terrainSize / 112f);
        var tileSize = terrainSize / tileCount;
        var halfSize = terrainSize * 0.5f;
        var hills = new List<MacroHill>(tileCount * tileCount * 3);
        for (var tileZ = 0; tileZ < tileCount; tileZ++)
        {
            for (var tileX = 0; tileX < tileCount; tileX++)
            {
                var tileCenter = new Vector2(
                    -halfSize + (tileX + 0.5f) * tileSize,
                    -halfSize + (tileZ + 0.5f) * tileSize);
                for (var hillIndex = 0; hillIndex < 3; hillIndex++)
                {
                    hills.Add(new MacroHill(
                        tileCenter + new Vector2(
                            random.Range(-tileSize * 0.34f, tileSize * 0.34f),
                            random.Range(-tileSize * 0.34f, tileSize * 0.34f)),
                        random.Range(tileSize * 0.24f, tileSize * 0.42f),
                        random.Range(0.17f, 0.27f)));
                }
            }
        }

        return hills.ToArray();
    }

    private static MountainPeak[] GenerateMountainPeaks(StableRandom random, float terrainSize)
    {
        var halfSize = terrainSize * 0.5f;
        if (terrainSize <= 140.01f)
        {
            var standardPeaks = new MountainPeak[6];
            for (var index = 0; index < standardPeaks.Length; index++)
            {
                var amount = index / (float)(standardPeaks.Length - 1);
                standardPeaks[index] = new MountainPeak(
                    new Vector2(
                        MathHelper.Lerp(-halfSize + 9f, halfSize - 9f, amount) + random.Range(-7f, 7f),
                        random.Range(-53f, -28f)),
                    new Vector2(random.Range(17f, 27f), random.Range(24f, 38f)),
                    random.Range(0.72f, 1.08f));
            }

            return standardPeaks;
        }

        var scale = terrainSize / 140f;
        var chainCount = Math.Max(2, (int)MathF.Round(MathF.Sqrt(scale)));
        var chainPeaks = new List<MountainPeak>();
        var radiusScale = MathF.Pow(scale, 0.25f);
        var amplitudeScale = 1f / MathF.Sqrt(radiusScale);
        for (var chain = 0; chain < chainCount; chain++)
        {
            var center = new Vector2(
                random.Range(-halfSize * 0.48f, halfSize * 0.48f),
                MathHelper.Lerp(-halfSize * 0.62f, halfSize * 0.62f, (chain + 0.5f) / chainCount) +
                random.Range(-halfSize * 0.13f, halfSize * 0.13f));
            var angle = random.Range(-1.08f, 1.08f) + (chain % 2 == 0 ? 0.38f : -0.38f);
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var perpendicular = new Vector2(-direction.Y, direction.X);
            var chainLength = terrainSize * random.Range(0.46f, 0.68f);
            var peaksPerChain = Math.Max(8, (int)MathF.Ceiling(chainLength / (34f * radiusScale)));
            var phase = random.Range(0f, MathHelper.TwoPi);
            var secondaryPhase = random.Range(0f, MathHelper.TwoPi);
            for (var index = 0; index < peaksPerChain; index++)
            {
                var amount = index / (float)(peaksPerChain - 1);
                var wandering = MathF.Sin(amount * MathHelper.TwoPi * 1.15f + phase) * terrainSize * 0.052f +
                                MathF.Sin(amount * MathHelper.TwoPi * 3.3f + secondaryPhase) * terrainSize * 0.016f;
                var peakCenter = center + direction * ((amount - 0.5f) * chainLength) +
                                 perpendicular * wandering +
                                 new Vector2(random.Range(-7f, 7f), random.Range(-7f, 7f)) * radiusScale;
                peakCenter = new Vector2(
                    MathHelper.Clamp(peakCenter.X, -halfSize + 16f, halfSize - 16f),
                    MathHelper.Clamp(peakCenter.Y, -halfSize + 16f, halfSize - 16f));
                chainPeaks.Add(new MountainPeak(
                    peakCenter,
                    new Vector2(
                        random.Range(24f, 36f) * radiusScale,
                        random.Range(28f, 43f) * radiusScale),
                    random.Range(0.72f, 1.08f) * amplitudeScale));
            }
        }

        return chainPeaks.ToArray();
    }

    private static float MountainMassAt(Vector2 position, IReadOnlyList<MountainPeak> peaks)
    {
        var mass = 0f;
        foreach (var peak in peaks)
        {
            var dx = (position.X - peak.Center.X) / peak.Radius.X;
            var dz = (position.Y - peak.Center.Y) / peak.Radius.Y;
            mass += MathF.Exp(-(dx * dx + dz * dz) * 1.42f) * peak.Amplitude;
        }

        return 1.34f * (1f - MathF.Exp(-mass * 0.86f));
    }

    private static MountainFormation[] GenerateMountainFormations(WorldSeed seed, TerrainSettings settings)
    {
        var random = new StableRandom(DeriveSeed(seed, "mountain-formations"));
        var terrainSize = settings.Size;
        if (terrainSize <= 140.01f)
        {
            return GenerateStandardMountainFormations(random, terrainSize);
        }

        var scale = terrainSize / 140f;
        var formationMultiplier = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(scale)));
        var formations = new List<MountainFormation>(formationMultiplier * 8);
        var halfSize = terrainSize * 0.5f;
        var gridSpacing = terrainSize / (settings.GridResolution - 1);

        // Three cross-slope fault lines build steep, irregular elevation steps. Each
        // transition deliberately spans several grid cells so the regular heightfield
        // resolves a continuous face instead of exposing its triangle diagonals.
        for (var cliffIndex = 0; cliffIndex < 3 * formationMultiplier; cliffIndex++)
        {
            var pointCount = Math.Max(10, formationMultiplier * 10);
            var points = new Vector2[pointCount];
            var baseZ = MathHelper.Lerp(-halfSize + 16f, halfSize - 16f,
                (cliffIndex + 0.5f) / (3f * formationMultiplier)) + random.Range(-5f, 5f);
            var phase = random.Range(0f, MathHelper.TwoPi);
            var wandering = random.Range(-1.2f, 1.2f);
            for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                var amount = pointIndex / (float)(pointCount - 1);
                wandering = MathHelper.Clamp(wandering + random.Range(-1.65f, 1.65f), -4.2f, 4.2f);
                points[pointIndex] = new Vector2(
                    MathHelper.Lerp(-halfSize - 2f, halfSize + 2f, amount),
                    baseZ + MathF.Sin(amount * MathHelper.TwoPi * 1.65f + phase) * 3.1f + wandering);
            }

            formations.Add(new MountainFormation(
                MountainFormationType.CliffFace,
                points,
                Math.Max(random.Range(2.35f, 2.90f) * MathF.Sqrt(scale), gridSpacing * 2.1f),
                random.Range(2.10f, 2.85f)));
        }

        // Two rare drainage cuts cross the whole range on long S-curves. Their
        // lateral travel is larger than their width by an order of magnitude,
        // avoiding the repeated near-vertical trench pattern.
        for (var ravineIndex = 0; ravineIndex < 2 * formationMultiplier; ravineIndex++)
        {
            var pointCount = Math.Max(19, formationMultiplier * 19);
            var points = new Vector2[pointCount];
            var phase = random.Range(0f, MathHelper.TwoPi);
            var secondaryPhase = random.Range(0f, MathHelper.TwoPi);
            var leftToRight = (ravineIndex & 1) == 0;
            var startX = leftToRight
                ? random.Range(-halfSize + 9f, -halfSize * 0.35f)
                : random.Range(halfSize * 0.35f, halfSize - 9f);
            var endX = leftToRight
                ? random.Range(halfSize * 0.08f, halfSize * 0.55f)
                : random.Range(-halfSize * 0.55f, -halfSize * 0.08f);
            for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                var amount = pointIndex / (float)(pointCount - 1);
                points[pointIndex] = new Vector2(
                    MathHelper.Lerp(startX, endX, amount) +
                    MathF.Sin(amount * MathHelper.TwoPi * 1.18f + phase) * 15f * MathF.Sqrt(scale) +
                    MathF.Sin(amount * MathHelper.Pi * 5.2f + secondaryPhase) * 3.2f,
                    MathHelper.Lerp(-halfSize - 1f, halfSize + 1f, amount) +
                    MathF.Sin(amount * MathHelper.Pi * 3.1f + secondaryPhase) * 3.1f);
            }

            formations.Add(new MountainFormation(
                MountainFormationType.Ravine,
                points,
                Math.Max(random.Range(1.15f, 1.65f) * MathF.Sqrt(scale), gridSpacing * 0.82f),
                random.Range(1.75f, 2.65f)));
        }

        // Broad drainage gullies run from the high range into the lower valleys.
        // They are shallow enough to read as erosion rather than more ravines.
        for (var drainageIndex = 0; drainageIndex < 3 * formationMultiplier; drainageIndex++)
        {
            var pointCount = Math.Max(15, formationMultiplier * 15);
            var points = new Vector2[pointCount];
            var phase = random.Range(0f, MathHelper.TwoPi);
            var baseX = MathHelper.Lerp(-halfSize + 24f, halfSize - 24f,
                drainageIndex / Math.Max(1f, 3f * formationMultiplier - 1f)) + random.Range(-8f, 8f);
            for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                var amount = pointIndex / (float)(pointCount - 1);
                points[pointIndex] = new Vector2(
                    baseX + MathF.Sin(amount * MathHelper.Pi * 1.55f + phase) * 9.5f +
                    MathF.Sin(amount * MathHelper.Pi * 4.4f + phase * 0.7f) * 2.2f,
                    MathHelper.Lerp(-halfSize + 3f, halfSize - 3f, amount) +
                    MathF.Sin(amount * MathHelper.Pi * 2.2f + phase) * 2.4f);
            }

            formations.Add(new MountainFormation(
                MountainFormationType.Drainage,
                points,
                Math.Max(random.Range(4.8f, 7.2f) * MathF.Sqrt(scale), gridSpacing * 2f),
                random.Range(0.85f, 1.45f)));
        }

        return formations.ToArray();
    }

    private static MountainFormation[] GenerateStandardMountainFormations(StableRandom random, float terrainSize)
    {
        var formations = new List<MountainFormation>(8);
        var halfSize = terrainSize * 0.5f;
        for (var cliffIndex = 0; cliffIndex < 3; cliffIndex++)
        {
            const int pointCount = 10;
            var points = new Vector2[pointCount];
            var baseZ = -13f - cliffIndex * 18f + random.Range(-2.6f, 2.6f);
            var phase = random.Range(0f, MathHelper.TwoPi);
            var wandering = random.Range(-1.2f, 1.2f);
            for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                var amount = pointIndex / (float)(pointCount - 1);
                wandering = MathHelper.Clamp(wandering + random.Range(-1.65f, 1.65f), -4.2f, 4.2f);
                points[pointIndex] = new Vector2(
                    MathHelper.Lerp(-halfSize - 2f, halfSize + 2f, amount),
                    baseZ + MathF.Sin(amount * MathHelper.TwoPi * 1.65f + phase) * 3.1f + wandering);
            }

            formations.Add(new MountainFormation(
                MountainFormationType.CliffFace,
                points,
                random.Range(2.35f, 2.90f),
                random.Range(2.10f, 2.85f)));
        }

        for (var ravineIndex = 0; ravineIndex < 2; ravineIndex++)
        {
            const int pointCount = 19;
            var points = new Vector2[pointCount];
            var phase = random.Range(0f, MathHelper.TwoPi);
            var secondaryPhase = random.Range(0f, MathHelper.TwoPi);
            var startX = ravineIndex == 0
                ? random.Range(-60f, -38f)
                : random.Range(38f, 60f);
            var endX = ravineIndex == 0
                ? random.Range(12f, 40f)
                : random.Range(-40f, -12f);
            for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                var amount = pointIndex / (float)(pointCount - 1);
                points[pointIndex] = new Vector2(
                    MathHelper.Lerp(startX, endX, amount) +
                    MathF.Sin(amount * MathHelper.TwoPi * 1.18f + phase) * 15f +
                    MathF.Sin(amount * MathHelper.Pi * 5.2f + secondaryPhase) * 3.2f,
                    MathHelper.Lerp(-halfSize - 1f, -3f, amount) +
                    MathF.Sin(amount * MathHelper.Pi * 3.1f + secondaryPhase) * 3.1f);
            }

            formations.Add(new MountainFormation(
                MountainFormationType.Ravine,
                points,
                random.Range(1.15f, 1.65f),
                random.Range(1.75f, 2.65f)));
        }

        for (var drainageIndex = 0; drainageIndex < 3; drainageIndex++)
        {
            const int pointCount = 15;
            var points = new Vector2[pointCount];
            var phase = random.Range(0f, MathHelper.TwoPi);
            var baseX = MathHelper.Lerp(-46f, 46f, drainageIndex / 2f) + random.Range(-6f, 6f);
            for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                var amount = pointIndex / (float)(pointCount - 1);
                points[pointIndex] = new Vector2(
                    baseX + MathF.Sin(amount * MathHelper.Pi * 1.55f + phase) * 9.5f +
                    MathF.Sin(amount * MathHelper.Pi * 4.4f + phase * 0.7f) * 2.2f,
                    MathHelper.Lerp(-halfSize + 3f, 3f, amount) +
                    MathF.Sin(amount * MathHelper.Pi * 2.2f + phase) * 2.4f);
            }

            formations.Add(new MountainFormation(
                MountainFormationType.Drainage,
                points,
                random.Range(4.8f, 7.2f),
                random.Range(0.85f, 1.45f)));
        }

        return formations.ToArray();
    }

    private static float ShapeMountain(
        Vector2 position,
        float baseHeight,
        float mountainInfluence,
        IReadOnlyList<MountainFormation> formations)
    {
        var formationBlend = SmoothStep(0.24f, 0.76f, mountainInfluence);
        if (formationBlend <= 0f)
        {
            return baseHeight;
        }

        var height = baseHeight;
        foreach (var formation in formations.Where(formation => formation.Type == MountainFormationType.CliffFace))
        {
            DistanceToPolyline(position, formation.Points, out var signedDistance);
            var step = 0.5f - SmootherStep(-formation.Width, formation.Width, signedDistance);
            height += step * formation.Strength * formationBlend;
        }

        foreach (var formation in formations.Where(formation => formation.Type == MountainFormationType.Ravine))
        {
            var distance = DistanceToPolyline(position, formation.Points, out _);
            var cut = 1f - SmoothStep(formation.Width * 0.06f, formation.Width, distance);
            var shoulder = SmoothStep(formation.Width * 0.68f, formation.Width, distance) *
                           (1f - SmoothStep(formation.Width, formation.Width * 1.42f, distance));
            height -= MathF.Pow(cut, 1.72f) * formation.Strength * formationBlend;
            height += shoulder * formation.Strength * 0.10f * formationBlend;
        }

        foreach (var formation in formations.Where(formation => formation.Type == MountainFormationType.Drainage))
        {
            var distance = DistanceToPolyline(position, formation.Points, out _);
            var channel = MathHelper.Clamp(
                1f - SmootherStep(formation.Width * 0.08f, formation.Width, distance),
                0f,
                1f);
            height -= MathF.Pow(channel, 1.28f) * formation.Strength * formationBlend;
        }

        return height;
    }

    private static float DistanceToPolyline(
        Vector2 point,
        IReadOnlyList<Vector2> points,
        out float signedDistance)
    {
        return DistanceToPolyline(point, points, out signedDistance, out _);
    }

    private static float DistanceToPolyline(
        Vector2 point,
        IReadOnlyList<Vector2> points,
        out float signedDistance,
        out float progress)
    {
        var closest = float.MaxValue;
        signedDistance = 0f;
        progress = 0f;
        var segmentLengths = new float[Math.Max(0, points.Count - 1)];
        var totalLength = 0f;
        for (var index = 0; index < segmentLengths.Length; index++)
        {
            segmentLengths[index] = Vector2.Distance(points[index], points[index + 1]);
            totalLength += segmentLengths[index];
        }
        var traversed = 0f;
        for (var index = 0; index < points.Count - 1; index++)
        {
            var start = points[index];
            var segment = points[index + 1] - start;
            var lengthSquared = segment.LengthSquared();
            var amount = lengthSquared < 0.0001f
                ? 0f
                : MathHelper.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0f, 1f);
            var nearest = start + segment * amount;
            var distance = Vector2.Distance(point, nearest);
            var segmentStart = traversed;
            traversed += segmentLengths[index];
            if (distance >= closest)
            {
                continue;
            }

            closest = distance;
            var length = MathF.Sqrt(Math.Max(lengthSquared, 0.0001f));
            signedDistance = (segment.X * (point.Y - start.Y) - segment.Y * (point.X - start.X)) / length;
            progress = totalLength <= 0.0001f ? 0f : (segmentStart + segmentLengths[index] * amount) / totalLength;
        }

        return closest;
    }

    private static LakeDefinition[] CarveLakes(WorldSeed seed, GeneratedTerrain terrain)
    {
        var random = new StableRandom(DeriveSeed(seed, "lakes"));
        LakeShape[] baseShapes =
        {
            new LakeShape(
                new Vector2(random.Range(36f, 50f), random.Range(23f, 36f)),
                new Vector2(random.Range(8.4f, 11.8f), random.Range(6.2f, 8.8f))),
            new LakeShape(
                new Vector2(random.Range(-52f, -38f), random.Range(8f, 21f)),
                new Vector2(random.Range(7.2f, 10.2f), random.Range(5.4f, 7.8f))),
            new LakeShape(
                new Vector2(random.Range(15f, 29f), random.Range(5f, 17f)),
                new Vector2(random.Range(5.8f, 8.4f), random.Range(4.5f, 6.8f))),
        };
        if (terrain.Settings.Size <= 140.01f)
        {
            return CarveLakeShapes(terrain, random, baseShapes);
        }

        var scale = terrain.Settings.Size / 140f;
        var lakeCount = Math.Max(3, (int)(3f * MathF.Sqrt(scale)));
        var halfSize = terrain.Settings.Size * 0.5f;
        var shapes = new LakeShape[lakeCount];
        for (var index = 0; index < shapes.Length; index++)
        {
            shapes[index] = new LakeShape(
                new Vector2(
                    random.Range(-halfSize + 18f, halfSize - 18f),
                    random.Range(-halfSize + 18f, halfSize - 18f)),
                new Vector2(random.Range(6f, 12f), random.Range(4.5f, 8.8f)));
        }

        return CarveLakeShapes(terrain, random, shapes);
    }

    private static LakeDefinition[] CarveLakeShapes(
        GeneratedTerrain terrain,
        StableRandom random,
        IReadOnlyList<LakeShape> shapes)
    {
        var lakes = new LakeDefinition[shapes.Count];

        for (var lakeIndex = 0; lakeIndex < shapes.Count; lakeIndex++)
        {
            var shape = shapes[lakeIndex];
            var waterHeight = terrain.SampleHeight(shape.Center.X, shape.Center.Y) - 0.42f;
            var waterRadius = shape.Radius * 0.78f;
            var shoreline = new Vector2[36];
            var phase = random.Range(0f, MathHelper.TwoPi);
            for (var pointIndex = 0; pointIndex < shoreline.Length; pointIndex++)
            {
                var angle = MathHelper.TwoPi * pointIndex / shoreline.Length;
                var irregularity = 1f + MathF.Sin(angle * 3f + phase) * 0.075f +
                                   MathF.Sin(angle * 7f - phase * 0.63f) * 0.038f +
                                   random.Range(-0.022f, 0.022f);
                shoreline[pointIndex] = shape.Center + new Vector2(
                    MathF.Cos(angle) * waterRadius.X * irregularity,
                    MathF.Sin(angle) * waterRadius.Y * irregularity);
            }

            lakes[lakeIndex] = new LakeDefinition(shape.Center, waterRadius, waterHeight, shoreline);
            for (var row = 0; row < terrain.Settings.GridResolution; row++)
            {
                for (var column = 0; column < terrain.Settings.GridResolution; column++)
                {
                    var position = terrain.PositionAt(column, row);
                    var dx = (position.X - shape.Center.X) / shape.Radius.X;
                    var dz = (position.Z - shape.Center.Y) / shape.Radius.Y;
                    var distance = MathF.Sqrt(dx * dx + dz * dz);
                    if (distance >= 1f)
                    {
                        continue;
                    }

                    var basinBlend = 1f - SmoothStep(0.76f, 1f, distance);
                    var basinHeight = waterHeight - 0.18f - (1f - distance) * 0.74f;
                    terrain.SetHeight(
                        column,
                        row,
                        Math.Min(position.Y, MathHelper.Lerp(position.Y, basinHeight, basinBlend)));
                }
            }
        }

        return lakes;
    }

    private static DungeonSite[] SelectDungeonSites(
        WorldSeed seed,
        GeneratedTerrain terrain,
        IReadOnlyList<DungeonChoice> dungeons,
        IReadOnlyList<LakeDefinition> lakes)
    {
        var spatialScale = terrain.Settings.Size / 140f;
        SiteZone[] zones =
        [
            new(new Vector2(-38f, 27f) * spatialScale, ScaleRectangle(new RectangleF(-54f, 16f, 28f, 24f), spatialScale)),
            new(new Vector2(0f, 2f) * spatialScale, ScaleRectangle(new RectangleF(-16f, -9f, 32f, 22f), spatialScale)),
            new(new Vector2(40f, -27f) * spatialScale, ScaleRectangle(new RectangleF(25f, -41f, 30f, 26f), spatialScale)),
        ];
        var random = new StableRandom(DeriveSeed(seed, "dungeon-sites"));
        var sites = new List<DungeonSite>(WorldGenerator.DungeonCount);

        for (var siteIndex = 0; siteIndex < zones.Length; siteIndex++)
        {
            var zone = zones[siteIndex];
            Vector3? bestPosition = null;
            var bestScore = float.MaxValue;
            for (var row = 3; row < terrain.Settings.GridResolution - 3; row++)
            {
                for (var column = 3; column < terrain.Settings.GridResolution - 3; column++)
                {
                    var position = terrain.PositionAt(column, row);
                    if (!zone.Bounds.Contains(position.X, position.Z))
                    {
                        continue;
                    }

                    var slope = SlopeDegrees(terrain.NormalAt(column, row));
                    var mountainInfluence = terrain.MountainInfluenceAt(column, row);
                    var wantsMountain = dungeons[siteIndex].Biome == Biome.Mountain;
                    if (slope > 18f ||
                        (wantsMountain && mountainInfluence < 0.28f) ||
                        (!wantsMountain && mountainInfluence > 0.48f) ||
                        IsInsideLake(position.X, position.Z, lakes, SiteClearRadius + 1.5f) ||
                        sites.Any(site => HorizontalDistance(site.Position, position) < 18f * spatialScale))
                    {
                        continue;
                    }

                    var distanceFromIdeal = Vector2.Distance(new Vector2(position.X, position.Z), zone.Ideal);
                    var score = slope * 0.34f + distanceFromIdeal * (0.12f / spatialScale) + random.Range(0f, 2.8f);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestPosition = position;
                    }
                }
            }

            // Every zone spans hundreds of samples. This fallback makes the exactly-three invariant
            // explicit even for a future, more rugged Hills definition.
            bestPosition ??= LowestSlopeSample(
                terrain,
                zone.Bounds,
                sites,
                lakes,
                dungeons[siteIndex].Biome == Biome.Mountain);
            var choice = dungeons[siteIndex];
            sites.Add(new DungeonSite(
                bestPosition.Value,
                choice.Number,
                choice.Name,
                random.Range(-0.14f, 0.14f),
                SiteClearRadius * MathF.Sqrt(spatialScale)));
        }

        return sites.ToArray();
    }

    private static RectangleF ScaleRectangle(RectangleF rectangle, float scale) => new(
        rectangle.X * scale,
        rectangle.Y * scale,
        rectangle.Width * scale,
        rectangle.Height * scale);

    private static Vector3 LowestSlopeSample(
        GeneratedTerrain terrain,
        RectangleF bounds,
        IReadOnlyList<DungeonSite> existingSites,
        IReadOnlyList<LakeDefinition> lakes,
        bool wantsMountain)
    {
        var minimumSiteDistance = 18f * terrain.Settings.Size / 140f;
        var bestSlope = float.MaxValue;
        var best = Vector3.Zero;
        for (var row = 3; row < terrain.Settings.GridResolution - 3; row++)
        {
            for (var column = 3; column < terrain.Settings.GridResolution - 3; column++)
            {
                var position = terrain.PositionAt(column, row);
                if (!bounds.Contains(position.X, position.Z) ||
                    (wantsMountain && terrain.MountainInfluenceAt(column, row) < 0.24f) ||
                    (!wantsMountain && terrain.MountainInfluenceAt(column, row) > 0.55f) ||
                    IsInsideLake(position.X, position.Z, lakes, SiteClearRadius + 1f) ||
                    existingSites.Any(site => HorizontalDistance(site.Position, position) < minimumSiteDistance))
                {
                    continue;
                }

                var slope = SlopeDegrees(terrain.NormalAt(column, row));
                if (slope < bestSlope)
                {
                    bestSlope = slope;
                    best = position;
                }
            }
        }

        return best;
    }

    private static void BlendSiteClearings(GeneratedTerrain terrain, IReadOnlyList<DungeonSite> sites)
    {
        foreach (var site in sites)
        {
            var targetHeight = terrain.SampleHeight(site.Position.X, site.Position.Z);
            for (var row = 0; row < terrain.Settings.GridResolution; row++)
            {
                for (var column = 0; column < terrain.Settings.GridResolution; column++)
                {
                    var position = terrain.PositionAt(column, row);
                    var distance = HorizontalDistance(position, site.Position);
                    var blend = 1f - SmoothStep(site.ClearedAreaRadius * 0.52f, site.ClearedAreaRadius * 1.38f, distance);
                    if (blend <= 0f)
                    {
                        continue;
                    }

                    terrain.SetHeight(column, row, MathHelper.Lerp(position.Y, targetHeight, blend * 0.96f));
                }
            }
        }
    }

    private static IReadOnlyList<TerrainFeature> GeneratePaths(
        WorldSeed seed,
        GeneratedTerrain terrain,
        IReadOnlyList<DungeonSite> sites)
    {
        var random = new StableRandom(DeriveSeed(seed, "worn-paths"));
        var features = new List<TerrainFeature>(sites.Count - 1);
        for (var pathIndex = 0; pathIndex < sites.Count - 1; pathIndex++)
        {
            var start = new Vector2(sites[pathIndex].Position.X, sites[pathIndex].Position.Z);
            var end = new Vector2(sites[pathIndex + 1].Position.X, sites[pathIndex + 1].Position.Z);
            var direction = end - start;
            direction.Normalize();
            var perpendicular = new Vector2(-direction.Y, direction.X);
            var bend = random.Range(-4.2f, 4.2f);
            var ripple = random.Range(-1.25f, 1.25f);
            var points = new Vector3[13];
            for (var pointIndex = 0; pointIndex < points.Length; pointIndex++)
            {
                var amount = pointIndex / (float)(points.Length - 1);
                var offset = MathF.Sin(amount * MathHelper.Pi) * bend + MathF.Sin(amount * MathHelper.TwoPi) * ripple;
                var point = Vector2.Lerp(start, end, amount) + perpendicular * offset;
                points[pointIndex] = new Vector3(point.X, terrain.SampleHeight(point.X, point.Y), point.Y);
            }

            points[0] = sites[pathIndex].Position;
            points[^1] = sites[pathIndex + 1].Position;

            features.Add(new TerrainFeature(
                TerrainFeatureType.WornPath,
                points,
                random.Range(1.25f, 1.75f),
                random.Range(-0.06f, 0.06f)));
        }

        return features;
    }

    private static IReadOnlyList<PropPlacement> Scatter(
        WorldSeed seed,
        string streamName,
        PropType type,
        ScatterDefinition rule,
        GeneratedTerrain terrain,
        IReadOnlyList<DungeonSite> sites,
        IReadOnlyList<TerrainFeature> features,
        IReadOnlyList<LakeDefinition> lakes,
        IReadOnlyList<PropPlacement> context)
    {
        var random = new StableRandom(DeriveSeed(seed, streamName));
        var forestPockets = new StablePerlinNoise(DeriveSeed(seed, "forest-pockets"));
        var categoryNoise = new StablePerlinNoise(DeriveSeed(seed, $"{streamName}-density"));
        var result = new List<PropPlacement>(rule.TargetCount);
        var halfSize = terrain.Settings.Size * 0.5f - 3f;
        var maximumAttempts = rule.TargetCount * 400;

        for (var attempt = 0; attempt < maximumAttempts && result.Count < rule.TargetCount; attempt++)
        {
            var x = random.Range(-halfSize, halfSize);
            var z = random.Range(-halfSize, halfSize);
            var height = terrain.SampleHeight(x, z);
            var normal = terrain.SampleNormal(x, z);
            var slope = SlopeDegrees(normal);
            var normalizedHeight = terrain.NormalizedHeight(height);
            var maximumMountainInfluence = type == PropType.Rock ? 0.52f : 0.275f;
            if (slope < rule.MinimumSlopeDegrees || slope > rule.MaximumSlopeDegrees ||
                normalizedHeight < rule.MinimumNormalizedHeight || normalizedHeight > rule.MaximumNormalizedHeight ||
                terrain.SampleMountainInfluence(x, z) > maximumMountainInfluence ||
                (type is PropType.Tree or PropType.Shrub &&
                 terrain.SampleMountainBand(x, z) != MountainElevationBand.None))
            {
                continue;
            }

            var candidate = new Vector3(x, height, z);
            var pathMargin = type switch
            {
                PropType.Tree => 1.65f,
                PropType.Shrub => 0.65f,
                _ => 0.35f,
            };
            var entranceMargin = type == PropType.Tree ? 4.6f : 1.15f;
            var lakeMargin = type == PropType.Tree ? 4.8f : type == PropType.Shrub ? 0.8f : 0.35f;
            if (sites.Any(site => HorizontalDistance(candidate, site.Position) < site.ClearedAreaRadius + entranceMargin) ||
                IsInsideLake(x, z, lakes, lakeMargin) ||
                result.Any(prop => HorizontalDistance(candidate, prop.Position) < rule.MinimumSpacing) ||
                DistanceToFeatures(candidate, features) < pathMargin)
            {
                continue;
            }

            var nearestContext = context.Count == 0
                ? float.MaxValue
                : context.Min(prop => HorizontalDistance(candidate, prop.Position));
            if (type == PropType.Shrub && nearestContext < 1.15f)
            {
                continue;
            }

            var pocket = forestPockets.Fractal(x * 0.031f, z * 0.031f, 3, 0.54f);
            var category = categoryNoise.Fractal(x * 0.067f, z * 0.067f, 2, 0.48f);
            var forestMask = terrain.SampleForestMask(x, z);
            var grassMask = terrain.SampleGrassMask(x, z);
            var screeMask = terrain.SampleScreeMask(x, z);
            var clearingMask = terrain.SampleClearingMask(x, z);
            var wetValleyMask = terrain.SampleWetValleyMask(x, z);
            var hillOutcropMask = terrain.SampleHillOutcropMask(x, z);
            if (type == PropType.Tree && (forestMask < 0.48f || clearingMask > 0.72f) ||
                type == PropType.Shrub && pocket < 0.455f && nearestContext > 6.5f ||
                type == PropType.Shrub && Math.Max(grassMask, clearingMask) < 0.16f && wetValleyMask < 0.18f ||
                type == PropType.Rock && category < 0.44f && Math.Max(hillOutcropMask, screeMask) < 0.18f)
            {
                continue;
            }

            var acceptance = AcceptanceFor(type, x, z, slope, normalizedHeight, pocket, category, nearestContext);
            acceptance *= type switch
            {
                PropType.Tree => 0.42f + forestMask * 1.60f + wetValleyMask * 0.28f,
                PropType.Shrub => 0.62f + Math.Max(Math.Max(grassMask, clearingMask), wetValleyMask) * 0.72f,
                PropType.Rock => 0.58f + Math.Max(hillOutcropMask, screeMask) * 0.94f,
                _ => 1f,
            };
            if (random.NextFloat() > acceptance)
            {
                continue;
            }

            var scale = ScaleFor(type, random, result.Count);
            var lean = type switch
            {
                PropType.Tree => random.Range(0.012f, 0.072f),
                PropType.Shrub => random.Range(0f, 0.045f),
                _ => random.Range(0f, 0.025f),
            };
            result.Add(new PropPlacement(
                type,
                candidate,
                random.Range(0f, MathHelper.TwoPi),
                scale,
                random.Range(-0.075f, 0.075f),
                normal,
                lean,
                random.Range(0f, MathHelper.TwoPi)));
        }

        return result;
    }

    private static IReadOnlyList<PropPlacement> ScatterMountain(
        WorldSeed seed,
        string streamName,
        PropType type,
        ScatterDefinition rule,
        GeneratedTerrain terrain,
        IReadOnlyList<DungeonSite> sites,
        IReadOnlyList<TerrainFeature> features,
        IReadOnlyList<LakeDefinition> lakes,
        IReadOnlyList<PropPlacement> context,
        params MountainElevationBand[] allowedBands)
    {
        var random = new StableRandom(DeriveSeed(seed, streamName));
        var densityNoise = new StablePerlinNoise(DeriveSeed(seed, "mountain-forest-bands"));
        var result = new List<PropPlacement>(rule.TargetCount);
        var halfSize = terrain.Settings.Size * 0.5f - 3f;
        var maximumAttempts = rule.TargetCount * 900;

        for (var attempt = 0; attempt < maximumAttempts && result.Count < rule.TargetCount; attempt++)
        {
            var x = random.Range(-halfSize, halfSize);
            var z = random.Range(-halfSize, terrain.Settings.Size <= 140.01f ? 4f : halfSize);
            var height = terrain.SampleHeight(x, z);
            var normal = terrain.SampleNormal(x, z);
            var slope = SlopeDegrees(normal);
            var normalizedHeight = terrain.NormalizedHeight(height);
            var surface = terrain.SampleSurface(x, z);
            var band = terrain.SampleMountainBand(x, z);
            // Wind-bent treeline pines may root in fractured rock where the soft forest mask
            // survives; their later mask gates still reject cliffs, scree and snow.
            var needsLivingGround = type == PropType.PineTree;
            if (terrain.SampleMountainInfluence(x, z) < 0.28f ||
                !allowedBands.Contains(band) ||
                (needsLivingGround && surface is not (TerrainSurface.MountainVegetatedRock or TerrainSurface.MountainVegetatedSnow)) ||
                slope < rule.MinimumSlopeDegrees || slope > rule.MaximumSlopeDegrees ||
                normalizedHeight < rule.MinimumNormalizedHeight || normalizedHeight > rule.MaximumNormalizedHeight)
            {
                continue;
            }

            if (type == PropType.PineTree &&
                (result.Count < 3 && band != MountainElevationBand.LowerSlope ||
                 band == MountainElevationBand.MidSlope &&
                 result.Count(prop => terrain.SampleMountainBand(prop.Position.X, prop.Position.Z) == MountainElevationBand.MidSlope) >=
                 rule.TargetCount / 3))
            {
                continue;
            }

            var candidate = new Vector3(x, height, z);
            var isTree = type is PropType.PineTree or PropType.CrookedPine or PropType.DeadConifer;
            var entranceMargin = isTree ? 4.1f : 1.15f;
            var pathMargin = isTree ? 1.5f : type == PropType.AlpineShrub ? 0.55f : 0.35f;
            var lakeMargin = isTree ? 3.4f : type == PropType.AlpineShrub ? 0.7f : 0.35f;
            if (sites.Any(site => HorizontalDistance(candidate, site.Position) < site.ClearedAreaRadius + entranceMargin) ||
                IsInsideLake(x, z, lakes, lakeMargin) ||
                result.Any(prop => HorizontalDistance(candidate, prop.Position) < rule.MinimumSpacing) ||
                DistanceToFeatures(candidate, features) < pathMargin)
            {
                continue;
            }

            var nearestContext = context.Count == 0
                ? float.MaxValue
                : context.Min(prop => HorizontalDistance(candidate, prop.Position));
            if (type == PropType.AlpineShrub && nearestContext < 0.9f)
            {
                continue;
            }

            var density = densityNoise.Fractal(x * 0.052f, z * 0.052f, 3, 0.51f);
            var forestMask = terrain.SampleForestMask(x, z);
            var rockMask = terrain.SampleRockMask(x, z);
            var screeMask = terrain.SampleScreeMask(x, z);
            var snowMask = terrain.SampleSnowMask(x, z);
            var grassMask = terrain.SampleGrassMask(x, z);
            if (type == PropType.PineTree && (density < 0.36f || forestMask < 0.015f) ||
                type == PropType.CrookedPine && (density < 0.26f || Math.Max(forestMask, grassMask) < 0.002f) ||
                type == PropType.AlpineShrub && Math.Max(grassMask, screeMask) < 0.002f ||
                type == PropType.PineTree &&
                (rockMask > 0.82f || screeMask > 0.76f || snowMask > 0.58f))
            {
                continue;
            }

            if (type == PropType.CrookedPine && (rockMask > 0.94f || screeMask > 0.76f || snowMask > 0.58f))
            {
                continue;
            }

            var nearTreeCover = 1f - SmoothStep(2.2f, 7.5f, nearestContext);
            var forestStrength = SmoothStep(0.49f, 0.69f, density);
            var acceptance = type switch
            {
                PropType.PineTree => 0.03f + forestStrength * 0.90f +
                                     (band == MountainElevationBand.LowerSlope ? 0.24f : 0f) -
                                     slope / 64f - normalizedHeight * 0.10f,
                PropType.CrookedPine => 0.18f + forestStrength * 0.68f +
                                       (band == MountainElevationBand.Treeline ? 0.30f : 0.10f) - slope / 92f,
                PropType.AlpineShrub => 0.17f + SmoothStep(0.40f, 0.62f, density) * 0.42f +
                                        nearTreeCover * (band == MountainElevationBand.Treeline ? 0.28f : 0.12f) - slope / 90f,
                PropType.LichenRock => 0.16f + SmoothStep(0.42f, 0.64f, density) * 0.44f +
                                       (surface is TerrainSurface.MountainRock or TerrainSurface.MountainSnow ? 0.18f : 0f) +
                                       slope / 100f,
                _ => 0.12f + SmoothStep(0.45f, 0.67f, density) * 0.36f + nearTreeCover * 0.30f - slope / 90f,
            };
            acceptance *= type switch
            {
                PropType.PineTree => 0.55f + forestMask * 1.55f,
                PropType.CrookedPine => 0.55f + forestMask * 1.30f,
                PropType.AlpineShrub => 0.65f + Math.Max(terrain.SampleGrassMask(x, z), screeMask) * 0.70f,
                PropType.LichenRock => 0.64f + Math.Max(rockMask, screeMask) * 0.80f,
                _ => 0.55f + forestMask * 1.10f,
            };
            if (random.NextFloat() > MathHelper.Clamp(acceptance, 0.04f, 0.92f))
            {
                continue;
            }

            var uniform = type switch
            {
                PropType.PineTree when result.Count < 3 => random.Range(1.18f, 1.40f),
                PropType.PineTree when result.Count < 11 => random.Range(0.52f, 0.72f),
                PropType.PineTree when band == MountainElevationBand.LowerSlope => random.Range(0.78f, 1.06f),
                PropType.PineTree => random.Range(0.62f, 0.88f),
                PropType.CrookedPine => random.Range(0.48f, 0.84f),
                PropType.AlpineShrub => random.Range(0.32f, 0.76f),
                PropType.LichenRock => random.Range(0.55f, 1.40f),
                _ => random.Range(0.65f, 1.10f),
            };
            var lean = type switch
            {
                PropType.CrookedPine => random.Range(0.09f, 0.22f),
                PropType.DeadConifer => random.Range(0.04f, 0.15f),
                PropType.PineTree => random.Range(0.008f, 0.06f),
                PropType.AlpineShrub => random.Range(0.005f, 0.038f),
                _ => random.Range(0f, 0.035f),
            };
            var xVariation = type == PropType.CrookedPine
                ? random.Range(0.72f, 0.96f)
                : random.Range(0.88f, 1.12f);
            var yVariation = type == PropType.CrookedPine
                ? random.Range(0.84f, 1.06f)
                : random.Range(0.90f, 1.12f);
            result.Add(new PropPlacement(
                type,
                candidate,
                random.Range(0f, MathHelper.TwoPi),
                new Vector3(
                    uniform * xVariation,
                    uniform * yVariation,
                    uniform * random.Range(0.88f, 1.12f)),
                random.Range(-0.07f, 0.065f),
                normal,
                lean,
                random.Range(0f, MathHelper.TwoPi)));
        }

        if (type == PropType.CrookedPine && result.Count < 6)
        {
            var resolution = terrain.Settings.GridResolution;
            var fallback = Enumerable.Range(0, terrain.Heights.Count)
                .Select(index =>
                {
                    var column = index % resolution;
                    var row = index / resolution;
                    var position = terrain.PositionAt(column, row);
                    var forest = terrain.ForestMaskAt(column, row);
                    var grass = terrain.GrassMaskAt(column, row);
                    var hazard = Math.Max(terrain.RockMaskAt(column, row),
                        Math.Max(terrain.ScreeMaskAt(column, row), terrain.SnowMaskAt(column, row)));
                    return (position, column, row, score: Math.Max(forest, grass) - hazard * 0.42f);
                })
                .OrderByDescending(sample => sample.score)
                .ToArray();
            foreach (var sample in fallback)
            {
                if (result.Count >= 6) break;
                var position = sample.position;
                var normal = terrain.NormalAt(sample.column, sample.row);
                var slope = SlopeDegrees(normal);
                var band = terrain.MountainBandAt(sample.column, sample.row);
                var normalizedHeight = terrain.NormalizedHeight(position.Y);
                if (!allowedBands.Contains(band) ||
                    slope < rule.MinimumSlopeDegrees || slope > rule.MaximumSlopeDegrees ||
                    normalizedHeight < rule.MinimumNormalizedHeight || normalizedHeight > rule.MaximumNormalizedHeight ||
                    terrain.DistanceToRidgeAt(sample.column, sample.row) < 4f ||
                    terrain.RockMaskAt(sample.column, sample.row) > 0.995f ||
                    terrain.ScreeDepositAt(sample.column, sample.row) > 0.70f ||
                    terrain.SnowMaskAt(sample.column, sample.row) > 0.90f ||
                    sites.Any(site => HorizontalDistance(position, site.Position) < site.ClearedAreaRadius + 4.1f) ||
                    IsInsideLake(position.X, position.Z, lakes, 3.4f) ||
                    DistanceToFeatures(position, features) < 1.5f ||
                    result.Any(prop => HorizontalDistance(position, prop.Position) < rule.MinimumSpacing))
                {
                    continue;
                }

                var uniform = random.Range(0.48f, 0.84f);
                result.Add(new PropPlacement(
                    PropType.CrookedPine,
                    position,
                    random.Range(0f, MathHelper.TwoPi),
                    new Vector3(uniform * random.Range(0.72f, 0.96f), uniform * random.Range(0.84f, 1.06f), uniform * random.Range(0.88f, 1.12f)),
                    random.Range(-0.07f, 0.065f),
                    normal,
                    random.Range(0.09f, 0.22f),
                    random.Range(0f, MathHelper.TwoPi)));
            }
        }

        if (type == PropType.AlpineShrub && result.Count < 8)
        {
            var resolution = terrain.Settings.GridResolution;
            var fallback = Enumerable.Range(0, terrain.Heights.Count)
                .Select(index => (index, mask: Math.Max(terrain.GrassMasks[index], terrain.ScreeMasks[index])))
                .OrderByDescending(sample => sample.mask)
                .ToArray();
            foreach (var sample in fallback)
            {
                if (result.Count >= 8) break;
                var column = sample.index % resolution;
                var row = sample.index / resolution;
                var position = terrain.PositionAt(column, row);
                var normal = terrain.NormalAt(column, row);
                var band = terrain.MountainBandAt(column, row);
                var slope = SlopeDegrees(normal);
                if (!allowedBands.Contains(band) || slope > rule.MaximumSlopeDegrees ||
                    sites.Any(site => HorizontalDistance(position, site.Position) < site.ClearedAreaRadius + 1.15f) ||
                    IsInsideLake(position.X, position.Z, lakes, 0.7f) ||
                    DistanceToFeatures(position, features) < 0.55f ||
                    result.Any(prop => HorizontalDistance(position, prop.Position) < rule.MinimumSpacing)) continue;

                var uniform = random.Range(0.32f, 0.76f);
                result.Add(new PropPlacement(
                    PropType.AlpineShrub, position, random.Range(0f, MathHelper.TwoPi),
                    new Vector3(uniform * random.Range(0.88f, 1.12f), uniform * random.Range(0.90f, 1.12f), uniform * random.Range(0.88f, 1.12f)),
                    random.Range(-0.07f, 0.065f), normal, random.Range(0.005f, 0.038f), random.Range(0f, MathHelper.TwoPi)));
            }
        }

        return result;
    }

    private static IReadOnlyList<PropPlacement> ScatterDeadwood(
        WorldSeed seed,
        GeneratedTerrain terrain,
        IReadOnlyList<DungeonSite> sites,
        IReadOnlyList<TerrainFeature> features,
        IReadOnlyList<LakeDefinition> lakes,
        IReadOnlyList<PropPlacement> trees)
    {
        PropType[] types = [PropType.DeadTree, PropType.FallenLog, PropType.Stump];
        var random = new StableRandom(DeriveSeed(seed, "deadwood"));
        var result = new List<PropPlacement>(types.Length);
        if (trees.Count == 0)
        {
            return result;
        }

        for (var typeIndex = 0; typeIndex < types.Length; typeIndex++)
        {
            for (var attempt = 0; attempt < 400; attempt++)
            {
                var anchor = trees[random.Next(trees.Count)];
                var angle = random.Range(0f, MathHelper.TwoPi);
                var distance = random.Range(3.2f, 6.8f);
                var x = anchor.Position.X + MathF.Cos(angle) * distance;
                var z = anchor.Position.Z + MathF.Sin(angle) * distance;
                var height = terrain.SampleHeight(x, z);
                var normal = terrain.SampleNormal(x, z);
                var candidate = new Vector3(x, height, z);
                if (SlopeDegrees(normal) > 18f ||
                    terrain.SampleMountainInfluence(x, z) >= 0.28f ||
                    IsInsideLake(x, z, lakes, 1f) ||
                    sites.Any(site => HorizontalDistance(candidate, site.Position) < site.ClearedAreaRadius + 1.2f) ||
                    result.Any(prop => HorizontalDistance(candidate, prop.Position) < 3f) ||
                    DistanceToFeatures(candidate, features) < 0.8f)
                {
                    continue;
                }

                var uniform = random.Range(0.82f, 1.22f);
                result.Add(new PropPlacement(
                    types[typeIndex],
                    candidate,
                    random.Range(0f, MathHelper.TwoPi),
                    new Vector3(
                        uniform * random.Range(0.90f, 1.10f),
                        uniform * random.Range(0.88f, 1.14f),
                        uniform * random.Range(0.90f, 1.10f)),
                    random.Range(-0.07f, 0.05f),
                    normal,
                    random.Range(0.025f, 0.09f),
                    random.Range(0f, MathHelper.TwoPi)));
                break;
            }
        }

        return result;
    }

    private static Vector3 ScaleFor(PropType type, StableRandom random, int placementIndex)
    {
        float uniform;
        if (type == PropType.Tree)
        {
            // Guarantee a natural hierarchy in every seed: a handful of old
            // canopy trees, a young understory, and mostly medium growth.
            uniform = placementIndex < 4
                ? random.Range(1.24f, 1.48f)
                : placementIndex < 14
                    ? random.Range(0.46f, 0.66f)
                    : random.Range(0.72f, 1.00f);
        }
        else
        {
            uniform = type == PropType.Shrub
                ? random.Range(0.38f, 1.02f)
                : random.Range(0.58f, 1.38f);
        }

        return new Vector3(
            uniform * random.Range(0.88f, 1.12f),
            uniform * random.Range(0.88f, 1.14f),
            uniform * random.Range(0.88f, 1.12f));
    }

    private static float AcceptanceFor(
        PropType type,
        float x,
        float z,
        float slope,
        float normalizedHeight,
        float pocket,
        float category,
        float nearestContext)
    {
        var pocketStrength = SmoothStep(0.54f, 0.72f, pocket);
        return type switch
        {
            PropType.Tree => MathHelper.Clamp(
                0.02f + pocketStrength * 0.94f + SmoothStep(2f, 22f, -z) * 0.12f +
                SmoothStep(22f, 38f, MathF.Abs(x)) * 0.12f -
                (z > 8f && MathF.Abs(x) < 19f ? 0.62f : 0f) -
                slope / 45f - normalizedHeight * 0.20f,
                0.01f,
                0.94f),
            PropType.Shrub => MathHelper.Clamp(
                0.03f + pocketStrength * 0.32f +
                (1f - SmoothStep(2.2f, 7.5f, nearestContext)) * 0.62f -
                slope / 58f - normalizedHeight * 0.10f,
                0.02f,
                0.93f),
            _ => MathHelper.Clamp(
                0.06f + SmoothStep(0.48f, 0.66f, category) * 0.58f +
                (1f - MathF.Abs(normalizedHeight - 0.54f) / 0.32f) * 0.25f + slope / 80f,
                0.02f,
                0.78f),
        };
    }

    private static float DistanceToFeatures(Vector3 position, IReadOnlyList<TerrainFeature> features)
    {
        var closest = float.MaxValue;
        var point = new Vector2(position.X, position.Z);
        foreach (var feature in features)
        {
            for (var index = 0; index < feature.Points.Count - 1; index++)
            {
                var start = new Vector2(feature.Points[index].X, feature.Points[index].Z);
                var end = new Vector2(feature.Points[index + 1].X, feature.Points[index + 1].Z);
                var segment = end - start;
                var amount = segment.LengthSquared() < 0.0001f
                    ? 0f
                    : MathHelper.Clamp(Vector2.Dot(point - start, segment) / segment.LengthSquared(), 0f, 1f);
                closest = Math.Min(closest, Vector2.Distance(point, start + segment * amount) - feature.Width * 0.5f);
            }
        }

        return closest;
    }

    private static void RecalculateTerrain(
        WorldSeed seed,
        GeneratedTerrain terrain,
        MountainBiomeDefinition mountains)
    {
        var vegetationNoise = new StablePerlinNoise(DeriveSeed(seed, "mountain-vegetation-zones"));
        var snowNoise = new StablePerlinNoise(DeriveSeed(seed, "mountain-snow-zones"));
        var maskNoise = new StablePerlinNoise(DeriveSeed(seed, "terrain-biome-masks"));
        var forestNoise = new StablePerlinNoise(DeriveSeed(seed, "hill-forest-density"));
        var clearingNoise = new StablePerlinNoise(DeriveSeed(seed, "hill-clearings"));
        var outcropNoise = new StablePerlinNoise(DeriveSeed(seed, "hill-outcrops"));
        var hillClearings = GenerateHillClearings(seed, terrain.Settings.Size);
        var resolution = terrain.Settings.GridResolution;
        var highestMountain = terrain.Heights
            .Where((_, index) => terrain.MountainInfluences[index] >= 0.28f)
            .Max();
        var effectiveSnowLine = Math.Min(mountains.SnowLineHeight, highestMountain - 1.2f);
        for (var row = 0; row < resolution; row++)
        {
            for (var column = 0; column < resolution; column++)
            {
                var leftFar = terrain.HeightAt(Math.Max(0, column - 2), row);
                var leftNear = terrain.HeightAt(Math.Max(0, column - 1), row);
                var rightNear = terrain.HeightAt(Math.Min(resolution - 1, column + 1), row);
                var rightFar = terrain.HeightAt(Math.Min(resolution - 1, column + 2), row);
                var nearFar = terrain.HeightAt(column, Math.Max(0, row - 2));
                var nearNear = terrain.HeightAt(column, Math.Max(0, row - 1));
                var farNear = terrain.HeightAt(column, Math.Min(resolution - 1, row + 1));
                var farFar = terrain.HeightAt(column, Math.Min(resolution - 1, row + 2));
                var normal = new Vector3(
                    leftFar + leftNear * 2f - rightNear * 2f - rightFar,
                    terrain.GridSpacing * 8f,
                    nearFar + nearNear * 2f - farNear * 2f - farFar);
                normal.Normalize();
                var slope = SlopeDegrees(normal);
                var height = terrain.HeightAt(column, row);
                var position = terrain.PositionAt(column, row);
                var normalizedHeight = terrain.NormalizedHeight(height);
                var variation = maskNoise.Fractal(position.X * 0.026f, position.Z * 0.026f, 3, 0.53f);
                var sunExposure = MathHelper.Clamp(normal.Y * 0.58f + normal.Z * 0.32f + normal.X * 0.10f, -1f, 1f) * 0.5f + 0.5f;
                var nearRidge = 1f - SmoothStep(1.8f, 10f, terrain.DistanceToRidgeAt(column, row));
                var nearDrainage = 1f - SmoothStep(1.0f, 8f, terrain.DistanceToDrainageAt(column, row));
                var slopeMask = SmoothStep(18f, 38f, slope);
                var rockMask = MathHelper.Clamp(slopeMask * 0.88f + nearRidge * 0.72f + (variation - 0.5f) * 0.20f, 0f, 1f);
                var fanDeposit = terrain.ScreeDepositAt(column, row);
                var screeMask = MathHelper.Clamp(
                    fanDeposit * (0.55f + SmoothStep(7f, 29f, slope) * 0.72f) *
                    (1f - SmoothStep(37f, 49f, slope)) +
                    SmoothStep(12f, 28f, slope) * (1f - slopeMask) * nearRidge * 0.12f,
                    0f,
                    1f);
                var coldExposure = 1f - sunExposure;
                var snowMask = MathHelper.Clamp(
                    SmoothStep(0.74f, 0.90f, normalizedHeight + (variation - 0.5f) * 0.08f) *
                    (0.55f + coldExposure * 0.45f) *
                    (0.72f + Math.Max(nearRidge, nearDrainage) * 0.28f) *
                    (1f - SmoothStep(28f, 40f, slope)),
                    0f,
                    1f);
                var sheltered = 1f - SmoothStep(0.48f, 0.78f, sunExposure + (variation - 0.5f) * 0.20f);
                var forestMask = MathHelper.Clamp(
                    SmoothStep(0.18f, 0.50f, normalizedHeight) *
                    (1f - SmoothStep(0.64f, 0.84f, normalizedHeight)) *
                    (1f - SmoothStep(15f, 32f, slope)) *
                    (0.58f + sheltered * 0.42f + nearDrainage * 0.48f) *
                    (1f - rockMask) * (1f - screeMask) * (1f - snowMask),
                    0f,
                    1f);
                var grassMask = MathHelper.Clamp(1f - Math.Max(Math.Max(rockMask, screeMask), Math.Max(snowMask, forestMask)), 0f, 1f);
                var geologyDirection = Vector2.Normalize(new Vector2(0.84f, 0.54f));
                var strataPhase = Vector2.Dot(new Vector2(position.X, position.Z), geologyDirection) * 0.29f + (variation - 0.5f) * 2.2f;
                var strataMask = (0.5f + MathF.Sin(strataPhase) * 0.5f) * rockMask;
                var hillBlend = 1f - SmoothStep(0.16f, 0.32f, terrain.MountainInfluenceAt(column, row));
                var hillRidge = 1f - SmoothStep(5f, 19f, terrain.DistanceToHillRidgeAt(column, row));
                var hillDrainage = 1f - SmoothStep(1.2f, 7.2f, terrain.DistanceToHillDrainageAt(column, row));
                var forestVariation = forestNoise.Fractal(position.X * 0.014f, position.Z * 0.014f, 3, 0.56f);
                var clearingVariation = clearingNoise.Fractal(position.X * 0.010f, position.Z * 0.010f, 2, 0.58f);
                var clearingMask = HillClearingMask(new Vector2(position.X, position.Z), hillClearings,
                    (clearingVariation - 0.5f) * 0.26f) *
                    (1f - SmoothStep(13f, 22f, slope)) * (0.72f + hillRidge * 0.28f) * hillBlend;
                var wetValleyMask = MathHelper.Clamp(
                    hillDrainage * (0.52f + (1f - sunExposure) * 0.28f + (1f - normalizedHeight) * 0.20f) * hillBlend,
                    0f, 1f);
                var hillOutcropMask = MathHelper.Clamp(
                    SmoothStep(0.56f, 0.74f, outcropNoise.Fractal(position.X * 0.025f, position.Z * 0.025f, 2, 0.50f)) *
                    SmoothStep(8f, 19f, slope) * hillRidge * hillBlend,
                    0f, 0.82f);
                var hillForestMask = MathHelper.Clamp(
                    (0.48f + forestVariation * 0.40f + wetValleyMask * 0.34f - hillRidge * 0.18f -
                     SmoothStep(17f, 29f, slope) * 0.52f - clearingMask * 0.96f - hillOutcropMask * 0.60f) * hillBlend,
                    0f, 1f);
                // Forest is the lowland base biome. Outside coherent clearing/outcrop regions,
                // retain a dense canopy floor so seed variation cannot collapse into grassland.
                var canopyReserve = (column * 73856093 ^ row * 19349663 ^ seed.Value * 83492791) & 1023;
                if (canopyReserve > 675 && hillBlend > 0.68f && clearingMask < 0.55f && hillOutcropMask < 0.42f && slope < 28f)
                {
                    hillForestMask = Math.Max(hillForestMask, 0.54f + wetValleyMask * 0.24f);
                }
                if (hillBlend > 0.01f)
                {
                    forestMask = Math.Max(forestMask, hillForestMask);
                    rockMask = Math.Max(rockMask, hillOutcropMask);
                    grassMask = MathHelper.Clamp(1f - Math.Max(forestMask, Math.Max(rockMask, clearingMask * 0.20f)), 0f, 1f);
                }
                terrain.SetHillAnalysis(column, row, terrain.DistanceToHillRidgeAt(column, row),
                    terrain.DistanceToHillDrainageAt(column, row), clearingMask, wetValleyMask, hillOutcropMask);
                terrain.SetMasks(column, row, sunExposure, rockMask, screeMask, snowMask, forestMask, grassMask, strataMask);
                TerrainSurface surface;
                var mountainBand = MountainElevationBand.None;
                if (terrain.MountainInfluenceAt(column, row) >= 0.28f)
                {
                    mountainBand = height < effectiveSnowLine - 8.0f
                        ? MountainElevationBand.LowerSlope
                        : height < effectiveSnowLine - 4.6f
                            ? MountainElevationBand.MidSlope
                            : height < effectiveSnowLine - 2.2f
                                ? MountainElevationBand.Treeline
                                : height < effectiveSnowLine
                                    ? MountainElevationBand.Alpine
                                    : MountainElevationBand.Peak;
                    var vegetation = vegetationNoise.Fractal(position.X * 0.045f, position.Z * 0.045f, 3, 0.52f);
                    var snowVariation = snowNoise.Fractal(position.X * 0.038f, position.Z * 0.038f, 3, 0.50f);
                    var vegetationBand = MathF.Sin(position.X * 0.17f + position.Z * 0.08f + seed.Value * 0.013f) +
                                         (vegetation - 0.5f) * 1.4f;
                    var vegetationThreshold = mountainBand switch
                    {
                        MountainElevationBand.LowerSlope => -0.48f,
                        MountainElevationBand.MidSlope => -0.12f,
                        MountainElevationBand.Treeline => 0.18f,
                        MountainElevationBand.Alpine => 0.28f,
                        _ => float.MaxValue,
                    };
                    var vegetated = vegetationBand > vegetationThreshold && slope < 35f;
                    var summitSnow = mountainBand == MountainElevationBand.Peak && snowMask > 0.42f && slope < 34f;
                    // Alpine shoulders are the soft transition into the sparse summit cap.
                    // Mask variation still controls the pixel blend within this whole band.
                    var snowDustedHeath = mountainBand == MountainElevationBand.Alpine;
                    surface = summitSnow
                        ? TerrainSurface.MountainSnow
                        : snowDustedHeath
                            ? TerrainSurface.MountainVegetatedSnow
                            : forestMask > 0.20f || vegetated && rockMask < 0.70f
                                ? TerrainSurface.MountainVegetatedRock
                                : TerrainSurface.MountainRock;
                }
                else
                {
                    surface = slope > 27f
                        ? TerrainSurface.SteepEarth
                        : height > 9.2f
                            ? TerrainSurface.RidgeGrass
                            : height < 3.85f && slope < 11f
                                ? TerrainSurface.LowGrass
                                : TerrainSurface.HillGrass;
                }

                terrain.SetDerived(column, row, normal, surface, mountainBand);
            }
        }
    }

    private static HillClearing[] GenerateHillClearings(WorldSeed seed, float terrainSize)
    {
        var random = new StableRandom(DeriveSeed(seed, "intentional-hill-clearings"));
        var scale = terrainSize / 140f;
        return
        [
            new HillClearing(new Vector2(random.Range(-42f, -25f), random.Range(18f, 36f)) * scale,
                new Vector2(random.Range(16f, 21f), random.Range(12f, 16f)) * MathF.Sqrt(scale)),
            new HillClearing(new Vector2(random.Range(-8f, 13f), random.Range(-2f, 18f)) * scale,
                new Vector2(random.Range(17f, 23f), random.Range(13f, 18f)) * MathF.Sqrt(scale)),
            new HillClearing(new Vector2(random.Range(27f, 46f), random.Range(10f, 31f)) * scale,
                new Vector2(random.Range(15f, 20f), random.Range(11f, 16f)) * MathF.Sqrt(scale)),
        ];
    }

    private static float HillClearingMask(Vector2 position, IReadOnlyList<HillClearing> clearings, float edgeWarp)
    {
        var mask = 0f;
        foreach (var clearing in clearings)
        {
            var relative = position - clearing.Center;
            var distance = MathF.Sqrt(
                relative.X * relative.X / (clearing.Radius.X * clearing.Radius.X) +
                relative.Y * relative.Y / (clearing.Radius.Y * clearing.Radius.Y));
            mask = Math.Max(mask, 1f - SmoothStep(0.62f + edgeWarp, 1.10f + edgeWarp, distance));
        }
        return MathHelper.Clamp(mask, 0f, 1f);
    }

    private static float ScreeFormationMask(Vector3 position, GeneratedTerrain terrain, float mountainInfluence)
    {
        if (mountainInfluence < 0.20f) return 0f;
        var normalizedHeight = terrain.NormalizedHeight(position.Y);
        return SmoothStep(0.38f, 0.62f, normalizedHeight) * (1f - SmoothStep(0.78f, 0.94f, normalizedHeight));
    }

    private static ulong DeriveSeed(WorldSeed seed, string streamName)
    {
        var hash = 14695981039346656037UL;
        foreach (var character in streamName)
        {
            hash ^= character;
            hash *= 1099511628211UL;
        }

        hash ^= unchecked((uint)seed.Value);
        hash *= 1099511628211UL;
        hash ^= hash >> 32;
        hash *= 0xd6e8feb86659fd93UL;
        return hash ^ (hash >> 32);
    }

    private static float SlopeDegrees(Vector3 normal) =>
        MathHelper.ToDegrees(MathF.Acos(MathHelper.Clamp(normal.Y, -1f, 1f)));

    private static float HorizontalDistance(Vector3 first, Vector3 second) =>
        Vector2.Distance(new Vector2(first.X, first.Z), new Vector2(second.X, second.Z));

    private static bool IsInsideLake(
        float x,
        float z,
        IReadOnlyList<LakeDefinition> lakes,
        float margin)
    {
        foreach (var lake in lakes)
        {
            var radiusX = lake.Radius.X + margin;
            var radiusZ = lake.Radius.Y + margin;
            var dx = (x - lake.Center.X) / radiusX;
            var dz = (z - lake.Center.Y) / radiusZ;
            if (dx * dx + dz * dz <= 1f)
            {
                return true;
            }
        }

        return false;
    }

    private static float SmoothStep(float from, float to, float value)
    {
        var amount = MathHelper.Clamp((value - from) / (to - from), 0f, 1f);
        return amount * amount * (3f - 2f * amount);
    }

    private static float SmootherStep(float from, float to, float value)
    {
        var amount = MathHelper.Clamp((value - from) / (to - from), 0f, 1f);
        return amount * amount * amount * (amount * (amount * 6f - 15f) + 10f);
    }

    private readonly record struct RidgeLine(
        IReadOnlyList<Vector2> Points,
        float CrestHeight,
        float Width,
        float Sharpness,
        float CrestPhase = 0f);
    private readonly record struct DrainageFlow(int[] Downhill, float[] Accumulation);
    private readonly record struct ScreeFan(IReadOnlyList<Vector2> Points, float Width);
    private readonly record struct MacroHill(Vector2 Center, float Radius, float Amplitude);
    private readonly record struct MountainPeak(Vector2 Center, Vector2 Radius, float Amplitude);
    private readonly record struct LakeShape(Vector2 Center, Vector2 Radius);
    private readonly record struct HillClearing(Vector2 Center, Vector2 Radius);
    private readonly record struct SiteZone(Vector2 Ideal, RectangleF Bounds);

    private readonly record struct RectangleF(float X, float Y, float Width, float Height)
    {
        public bool Contains(float x, float y) => x >= X && x <= X + Width && y >= Y && y <= Y + Height;
    }
}
