using System;
using Microsoft.Xna.Framework;

namespace Game;

/// <summary>
/// Shared aerial-landscape lighting constants and CPU-side terrain shading. Geometry uses the
/// same sun and hemisphere palette through BasicEffect; the terrain bake additionally applies
/// generated horizon occlusion and a compact filmic curve before it reaches the LDR target.
/// </summary>
public static class TerrainLighting
{
    public const float SunElevationDegrees = 35f;

    // Direction in which sunlight travels. Its diagonal projection crosses the generated ridge
    // graph instead of following it, making both the crests and drainage ribs readable.
    public static readonly Vector3 SunDirection = Vector3.Normalize(new Vector3(-0.579f, -0.574f, -0.579f));
    public static readonly Vector3 SunColor = new(1.00f, 0.88f, 0.68f);
    public static readonly Vector3 SkyAmbient = new(0.34f, 0.42f, 0.50f);
    public static readonly Vector3 GroundAmbient = new(0.17f, 0.18f, 0.13f);
    public static readonly Vector3 FogColor = new(0.55f, 0.61f, 0.64f);

    public static Color ShadeTerrain(Color albedo, Vector3 normal, float ambientAccessibility, float sunVisibility)
    {
        if (normal.LengthSquared() < 0.0001f)
        {
            normal = Vector3.Up;
        }
        else
        {
            normal.Normalize();
        }

        var hemisphereAmount = normal.Y * 0.5f + 0.5f;
        var hemisphere = Vector3.Lerp(GroundAmbient, SkyAmbient, hemisphereAmount);
        var accessibility = MathHelper.Lerp(0.46f, 1f, MathHelper.Clamp(ambientAccessibility, 0f, 1f));
        hemisphere *= accessibility;

        var incidence = Math.Max(0f, Vector3.Dot(normal, -SunDirection));
        // A wide penumbra keeps the terminator soft while retaining strong warm/cool relief.
        var penumbra = SmoothStep(0.015f, 0.34f, incidence);
        var direct = SunColor * (penumbra * MathHelper.Lerp(0.28f, 0.62f, incidence) *
                                 MathHelper.Lerp(0.16f, 1f, MathHelper.Clamp(sunVisibility, 0f, 1f)));
        var baseColor = albedo.ToVector3();
        var hdr = Multiply(baseColor, hemisphere + direct) * 0.82f;
        var mapped = AcesApproximate(hdr);
        var luminance = Vector3.Dot(mapped, new Vector3(0.2126f, 0.7152f, 0.0722f));
        mapped = Vector3.Lerp(new Vector3(luminance), mapped, 1.08f);
        mapped = (mapped - new Vector3(0.5f)) * 1.07f + new Vector3(0.5f);
        mapped = Vector3.Clamp(mapped, Vector3.Zero, Vector3.One);
        return new Color(mapped.X, mapped.Y, mapped.Z, albedo.A / 255f);
    }

    public static Vector2 ShadowGroundDirection
    {
        get
        {
            var ground = new Vector2(SunDirection.X, SunDirection.Z);
            ground.Normalize();
            return ground;
        }
    }

    public static float SnowCoverage(float snowAnalysis, float normalizedHeight, float boundaryNoise, float slopeDegrees)
    {
        // Both the analysis and altitude terms are continuous. Keeping this independent of the
        // discrete TerrainSurface enum prevents grid cells from appearing as square snow tiles.
        var analysisCoverage = SmoothStep(0.035f, 0.58f, snowAnalysis);
        var altitudeCoverage = SmoothStep(0.66f, 0.88f, normalizedHeight + (boundaryNoise - 0.5f) * 0.055f);
        var slopeRetention = 1f - SmoothStep(25f, 40f, slopeDegrees) * 0.44f;
        return MathHelper.Clamp(
            analysisCoverage * MathHelper.Lerp(0.30f, 0.92f, altitudeCoverage) * slopeRetention,
            0f,
            0.92f);
    }

    private static Vector3 AcesApproximate(Vector3 color) => new(
        AcesChannel(color.X),
        AcesChannel(color.Y),
        AcesChannel(color.Z));

    private static float AcesChannel(float value)
    {
        const float a = 2.51f;
        const float b = 0.03f;
        const float c = 2.43f;
        const float d = 0.59f;
        const float e = 0.14f;
        return MathHelper.Clamp((value * (a * value + b)) / (value * (c * value + d) + e), 0f, 1f);
    }

    private static Vector3 Multiply(Vector3 first, Vector3 second) => new(
        first.X * second.X,
        first.Y * second.Y,
        first.Z * second.Z);

    private static float SmoothStep(float from, float to, float value)
    {
        var amount = MathHelper.Clamp((value - from) / (to - from), 0f, 1f);
        return amount * amount * (3f - 2f * amount);
    }
}
