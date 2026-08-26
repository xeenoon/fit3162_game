using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Game;

/// <summary>
/// Bakes the tileable terrain material library into one seed-specific map. The bake keeps the
/// renderer to a single terrain draw while still allowing every generated surface to select and
/// softly blend its own source materials.
/// </summary>
internal static class TerrainMaterialTexture
{
    // The diorama is 480 pixels tall; a 512-square bake preserves visible ground detail while
    // keeping material composition out of the frame loop and comfortably inside startup budgets.
    private const int TextureSize = 512;

    public static Texture2D Create(GraphicsDevice graphicsDevice, GeneratedWorld world)
    {
        var root = FindTextureRoot();
        var materials = MaterialSet.Load(
            graphicsDevice,
            root,
            world.Seed,
            world.TerrainSettings.Size / TextureSize);
        var pixels = Compose(world, materials, TextureSize);
        var texture = new Texture2D(graphicsDevice, TextureSize, TextureSize, false, SurfaceFormat.Color);
        texture.SetData(pixels);
        return texture;
    }

    private static Color[] Compose(GeneratedWorld world, MaterialSet materials, int size)
    {
        var pixels = new Color[size * size];
        var terrain = world.Terrain;
        var worldSize = terrain.Settings.Size;
        var halfSize = worldSize * 0.5f;
        var detailStrength = 1f - SmoothStep(0.55f, 2.25f, worldSize / size);
        var treeDensity = BuildDensityMap(world, size, 10f, PropType.Tree);
        var pineDensity = BuildDensityMap(world, size, 9f, PropType.PineTree, PropType.CrookedPine);
        var rockDensity = BuildDensityMap(world, size, 7f, PropType.Rock, PropType.LichenRock);
        var seed = unchecked((ulong)(uint)world.Seed);
        var boundaryNoise = new StablePerlinNoise(seed ^ 0x9ce7431b52ad806fUL);
        var moistureNoise = new StablePerlinNoise(seed ^ 0x6a51bf20e39c784dUL);

        for (var row = 0; row < size; row++)
        {
            var worldZ = MathHelper.Lerp(-halfSize, halfSize, (row + 0.5f) / size);
            for (var column = 0; column < size; column++)
            {
                var worldX = MathHelper.Lerp(-halfSize, halfSize, (column + 0.5f) / size);
                var index = row * size + column;
                var height = terrain.SampleHeight(worldX, worldZ);
                var normal = terrain.SampleNormal(worldX, worldZ);
                var slope = MathHelper.ToDegrees(MathF.Acos(MathHelper.Clamp(normal.Y, -1f, 1f)));
                var mountainInfluence = terrain.SampleMountainInfluence(worldX, worldZ);
                var rockAnalysis = terrain.SampleRockMask(worldX, worldZ);
                var screeAnalysis = terrain.SampleScreeMask(worldX, worldZ);
                var screeDeposit = terrain.SampleScreeDeposit(worldX, worldZ);
                var alluvialDeposit = terrain.SampleAlluvialDeposit(worldX, worldZ);
                var snowAnalysis = terrain.SampleSnowMask(worldX, worldZ);
                var forestAnalysis = terrain.SampleForestMask(worldX, worldZ);
                var strataAnalysis = terrain.SampleStrataMask(worldX, worldZ);
                var clearingAnalysis = terrain.SampleClearingMask(worldX, worldZ);
                var wetValleyAnalysis = terrain.SampleWetValleyMask(worldX, worldZ);
                var hillOutcropAnalysis = terrain.SampleHillOutcropMask(worldX, worldZ);
                var flow = terrain.SampleFlowAccumulation(worldX, worldZ);
                var flowWeight = SmoothStep(2.2f, 7.2f, MathF.Log(MathF.Max(1f, flow)));
                var curvature = terrain.SampleCurvature(worldX, worldZ) * terrain.GridSpacing;
                var convexRidge = SmoothStep(0.025f, 0.30f, -curvature);
                var concaveValley = SmoothStep(0.025f, 0.30f, curvature);
                var normalizedHeight = terrain.NormalizedHeight(height);
                var boundary = boundaryNoise.Fractal(worldX * 0.033f, worldZ * 0.033f, 3, 0.51f);
                var moisture = moistureNoise.Fractal(worldX * 0.021f, worldZ * 0.021f, 3, 0.54f);
                var macroA = boundaryNoise.Fractal(worldX * 0.0105f, worldZ * 0.0105f, 2, 0.58f);
                var macroB = moistureNoise.Fractal(worldX * 0.0075f + 17f, worldZ * 0.0075f - 29f, 2, 0.61f);
                var pondDistance = DistanceToLakes(world.Lakes, worldX, worldZ);
                var pathDistance = DistanceToFeatures(world.Features, worldX, worldZ);

                var hillMacro = materials.HillMacro.Sample(worldX, worldZ);
                var grass = materials.Grass.Sample(worldX, worldZ);
                var hills = Color.Lerp(hillMacro, grass, 0.72f * detailStrength);

                // Broad, overlapping zones are deliberately much larger than the detail repeat.
                // They break the foreground into readable dry ridges, damp basins, and olive scrub.
                var dryZone = MathHelper.Clamp(
                    SmoothStep(0.53f, 0.70f, macroA + normalizedHeight * 0.18f - moisture * 0.12f) * 0.58f +
                    convexRidge * 0.62f,
                    0f, 1f);
                var dampZone = MathHelper.Clamp(
                    (1f - SmoothStep(2.5f, 14f, pondDistance)) * 0.78f +
                    SmoothStep(0.54f, 0.72f, macroB) * (1f - normalizedHeight) * 0.28f +
                    concaveValley * 0.34f + flowWeight * 0.48f,
                    0f,
                    0.88f);
                var scrubZone = SmoothStep(0.50f, 0.68f, macroA * 0.55f + boundary * 0.45f) *
                                (1f - dampZone) * (1f - dryZone * 0.45f);
                hills = Color.Lerp(hills, new Color(133, 119, 78), dryZone * 0.34f);
                hills = Color.Lerp(hills, new Color(55, 72, 62), dampZone * 0.38f);
                hills = Color.Lerp(hills, new Color(82, 91, 61), scrubZone * 0.20f);
                hills = Color.Lerp(hills, new Color(151, 132, 83), clearingAnalysis * 0.46f);
                hills = Color.Lerp(hills, new Color(47, 65, 54), wetValleyAnalysis * 0.48f);
                hills = Color.Lerp(
                    hills,
                    materials.SharedRockyGround.Sample(worldX + 2.9f, worldZ - 4.6f),
                    flowWeight * (0.28f + concaveValley * 0.34f) * detailStrength);
                hills = Color.Lerp(hills, new Color(105, 92, 68), alluvialDeposit * 0.62f);

                var forestPocket = SmoothStep(0.46f, 0.70f, moisture + (boundary - 0.5f) * 0.28f);
                var forestWeight = MathHelper.Clamp(
                    treeDensity[index] * 0.62f + forestPocket * 0.22f + forestAnalysis * 0.72f,
                    0f,
                    0.88f) * (1f - SmoothStep(12f, 24f, slope));
                hills = Color.Lerp(hills, materials.ForestGround.Sample(worldX, worldZ), forestWeight * detailStrength);
                hills = Color.Lerp(
                    hills,
                    materials.SharedRockyGround.Sample(worldX - 3.7f, worldZ + 6.2f),
                    hillOutcropAnalysis * 0.72f * detailStrength);

                var clearingWeight = DungeonClearingMask(world.DungeonSites, worldX, worldZ);
                var thinGrass = MathHelper.Clamp(
                    SmoothStep(10f, 28f, slope) * 0.76f +
                    SmoothStep(0.64f, 0.84f, normalizedHeight + (boundary - 0.5f) * 0.18f) * 0.32f +
                    clearingWeight + rockDensity[index] * 0.32f,
                    0f,
                    0.92f);
                hills = Color.Lerp(hills, materials.SharedRockyGround.Sample(worldX, worldZ), thinGrass * detailStrength);

                var wornPath = 1f - SmoothStep(0.45f, 3.4f, pathDistance);
                var pathShoulder = 1f - SmoothStep(1.2f, 5.6f, pathDistance);
                hills = Color.Lerp(
                    hills,
                    materials.SharedRockyGround.Sample(worldX + 8.3f, worldZ - 5.1f),
                    pathShoulder * 0.48f * detailStrength);
                hills = Color.Lerp(hills, new Color(91, 76, 59), wornPath * 0.52f);

                var mountainMacro = materials.MountainMacro.Sample(worldX, worldZ);
                var rockWeight = MathHelper.Clamp(
                    SmoothStep(12f, 32f, slope) * 0.54f + 0.06f + rockAnalysis * 0.66f +
                    convexRidge * 0.18f + snowAnalysis * 0.20f,
                    0f,
                    0.94f);
                var mountains = Color.Lerp(
                    mountainMacro,
                    materials.DarkRock.Sample(worldX, height, worldZ, normal),
                    rockWeight * detailStrength);
                mountains = Tint(mountains, (strataAnalysis - 0.5f) * rockAnalysis * 0.12f);

                var cliffApron = FormationMask(world.MountainFormations, worldX, worldZ, MountainFormationType.CliffFace);
                var drainage = FormationMask(world.MountainFormations, worldX, worldZ, MountainFormationType.Drainage);
                mountains = Color.Lerp(
                    mountains,
                    materials.Scree.Sample(worldX + 4.1f, worldZ - 2.7f),
                    SmoothStep(0.04f, 0.50f, drainage) * 0.34f * detailStrength);
                var drainageCore = SmoothStep(0.52f, 0.90f, drainage);
                drainageCore = Math.Max(drainageCore, flowWeight * concaveValley);
                mountains = Color.Lerp(mountains, new Color(43, 48, 48), drainageCore * 0.74f * detailStrength);
                var screeWeight = MathHelper.Clamp(
                    cliffApron * SmoothStep(5f, 25f, slope) * (0.34f + boundary * 0.28f) +
                    drainage * 0.20f + screeAnalysis * 0.82f + screeDeposit * 0.34f,
                    0f,
                    0.82f);
                mountains = Color.Lerp(
                    mountains,
                    materials.Scree.Sample(worldX, worldZ),
                    screeWeight * detailStrength);

                var lichenWeight = MathHelper.Clamp(
                    SmoothStep(0.50f, 0.72f, moisture) *
                    (1f - SmoothStep(0.68f, 0.88f, normalizedHeight)) *
                    (1f - SmoothStep(24f, 38f, slope)) * 0.58f,
                    0f,
                    0.58f);
                mountains = Color.Lerp(
                    mountains,
                    materials.LichenRock.Sample(worldX, worldZ),
                    lichenWeight * detailStrength);

                var coniferFloorWeight = MathHelper.Clamp(pineDensity[index] * 0.72f, 0f, 0.76f) *
                                          (1f - SmoothStep(22f, 34f, slope));
                mountains = Color.Lerp(
                    mountains,
                    materials.ConiferGround.Sample(worldX, worldZ),
                    coniferFloorWeight * detailStrength);

                // Snow is a continuous analysis field. Do not gate it through the discrete
                // TerrainSurface classification: that exposes heightfield cells as square white
                // fragments when the material bake is viewed at 1080p.
                var snowWeight = TerrainLighting.SnowCoverage(snowAnalysis, normalizedHeight, boundary, slope);
                var snow = Color.Lerp(
                    materials.SnowMacro.Sample(worldX, worldZ),
                    materials.SnowDetail.Sample(worldX, worldZ),
                    0.68f * detailStrength);
                mountains = Color.Lerp(mountains, snow, snowWeight);

                var biomeBlend = SmoothStep(
                    0.12f,
                    0.64f,
                    mountainInfluence + (boundary - 0.5f) * 0.16f);
                var albedo = Color.Lerp(hills, mountains, biomeBlend);
                pixels[index] = TerrainLighting.ShadeTerrain(
                    albedo,
                    normal,
                    terrain.SampleAmbientAccessibility(worldX, worldZ),
                    terrain.SampleSunVisibility(worldX, worldZ));
            }
        }

        return pixels;
    }

    private static Color Tint(Color color, float amount)
    {
        var multiplier = 1f + amount;
        return new Color(
            (byte)Math.Clamp((int)(color.R * multiplier), 0, 255),
            (byte)Math.Clamp((int)(color.G * multiplier), 0, 255),
            (byte)Math.Clamp((int)(color.B * multiplier), 0, 255),
            color.A);
    }

    private static float[] BuildDensityMap(
        GeneratedWorld world,
        int size,
        float radius,
        params PropType[] types)
    {
        var density = new float[size * size];
        var halfSize = world.TerrainSettings.Size * 0.5f;
        var pixelsPerWorldUnit = size / world.TerrainSettings.Size;
        var pixelRadius = Math.Max(1, (int)MathF.Ceiling(radius * pixelsPerWorldUnit));
        var accepted = types.ToHashSet();
        foreach (var prop in world.Props.Where(prop => accepted.Contains(prop.Type)))
        {
            var centerX = (prop.Position.X + halfSize) * pixelsPerWorldUnit;
            var centerY = (prop.Position.Z + halfSize) * pixelsPerWorldUnit;
            var left = Math.Max(0, (int)MathF.Floor(centerX - pixelRadius));
            var right = Math.Min(size - 1, (int)MathF.Ceiling(centerX + pixelRadius));
            var top = Math.Max(0, (int)MathF.Floor(centerY - pixelRadius));
            var bottom = Math.Min(size - 1, (int)MathF.Ceiling(centerY + pixelRadius));
            for (var row = top; row <= bottom; row++)
            {
                var deltaZ = (row + 0.5f - centerY) / pixelRadius;
                for (var column = left; column <= right; column++)
                {
                    var deltaX = (column + 0.5f - centerX) / pixelRadius;
                    var distanceSquared = deltaX * deltaX + deltaZ * deltaZ;
                    if (distanceSquared <= 1f)
                    {
                        var index = row * size + column;
                        density[index] = MathHelper.Clamp(
                            density[index] + MathF.Exp(-distanceSquared * 3.4f) * 0.74f,
                            0f,
                            1f);
                    }
                }
            }
        }

        return density;
    }

    private static float DungeonClearingMask(IReadOnlyList<DungeonSite> sites, float worldX, float worldZ)
    {
        var mask = 0f;
        foreach (var site in sites)
        {
            var distance = Vector2.Distance(new Vector2(worldX, worldZ), new Vector2(site.Position.X, site.Position.Z));
            mask = Math.Max(mask, 1f - SmoothStep(site.ClearedAreaRadius * 0.62f, site.ClearedAreaRadius + 1.8f, distance));
        }

        return mask * 0.82f;
    }

    private static float DistanceToLakes(IReadOnlyList<LakeDefinition> lakes, float worldX, float worldZ)
    {
        var closest = float.MaxValue;
        var point = new Vector2(worldX, worldZ);
        foreach (var lake in lakes)
        {
            foreach (var shoreline in lake.Shoreline)
            {
                closest = Math.Min(closest, Vector2.Distance(point, shoreline));
            }
        }

        return closest;
    }

    private static float DistanceToFeatures(IReadOnlyList<TerrainFeature> features, float worldX, float worldZ)
    {
        var point = new Vector2(worldX, worldZ);
        var closest = float.MaxValue;
        foreach (var feature in features)
        {
            var points = feature.Points.Select(position => new Vector2(position.X, position.Z)).ToArray();
            closest = Math.Min(closest, DistanceToPolyline(point, points) - feature.Width * 0.5f);
        }

        return Math.Max(0f, closest);
    }

    private static float FormationMask(
        IReadOnlyList<MountainFormation> formations,
        float worldX,
        float worldZ,
        MountainFormationType type)
    {
        var position = new Vector2(worldX, worldZ);
        var mask = 0f;
        foreach (var formation in formations)
        {
            if (formation.Type != type)
            {
                continue;
            }

            var distance = DistanceToPolyline(position, formation.Points);
            mask = Math.Max(mask, 1f - SmoothStep(formation.Width * 0.70f, formation.Width * 2.5f, distance));
        }

        return mask;
    }

    private static float DistanceToPolyline(Vector2 point, IReadOnlyList<Vector2> points)
    {
        var closest = float.MaxValue;
        for (var index = 0; index < points.Count - 1; index++)
        {
            var start = points[index];
            var segment = points[index + 1] - start;
            var amount = segment.LengthSquared() < 0.0001f
                ? 0f
                : MathHelper.Clamp(Vector2.Dot(point - start, segment) / segment.LengthSquared(), 0f, 1f);
            closest = Math.Min(closest, Vector2.Distance(point, start + segment * amount));
        }

        return closest;
    }

    private static string FindTextureRoot()
    {
        var outputRoot = Path.Combine(AppContext.BaseDirectory, "textures");
        if (Directory.Exists(outputRoot))
        {
            return outputRoot;
        }

        var workingRoot = Path.Combine(Directory.GetCurrentDirectory(), "textures");
        if (Directory.Exists(workingRoot))
        {
            return workingRoot;
        }

        throw new DirectoryNotFoundException(
            $"Terrain texture library was not found at '{outputRoot}' or '{workingRoot}'.");
    }

    private static float SmoothStep(float from, float to, float value)
    {
        var amount = MathHelper.Clamp((value - from) / (to - from), 0f, 1f);
        return amount * amount * (3f - 2f * amount);
    }

    private sealed class MaterialSet
    {
        private MaterialSet(MaterialLayer[] layers)
        {
            HillMacro = layers[0];
            Grass = layers[1];
            ForestGround = layers[2];
            ConiferGround = layers[3];
            MountainMacro = layers[4];
            SnowMacro = layers[5];
            DarkRock = layers[6];
            Scree = layers[7];
            LichenRock = layers[8];
            SnowDetail = layers[9];
            SharedRockyGround = layers[10];
        }

        public MaterialLayer HillMacro { get; }
        public MaterialLayer Grass { get; }
        public MaterialLayer ForestGround { get; }
        public MaterialLayer ConiferGround { get; }
        public MaterialLayer MountainMacro { get; }
        public MaterialLayer SnowMacro { get; }
        public MaterialLayer DarkRock { get; }
        public MaterialLayer Scree { get; }
        public MaterialLayer LichenRock { get; }
        public MaterialLayer SnowDetail { get; }
        public MaterialLayer SharedRockyGround { get; }

        public static MaterialSet Load(
            GraphicsDevice graphicsDevice,
            string root,
            int seed,
            float texelWorldSize)
        {
            string[] relativePaths =
            [
                "hills/macro/aerial_grass_rock/aerial_grass_rock_diff_1k.jpg",
                "hills/detail/sparse_grass/sparse_grass_diff_1k.jpg",
                "hills/detail/forest_ground_04/forest_ground_04_diff_1k.jpg",
                "hills/detail/forrest_ground_03/forrest_ground_03_diff_1k.jpg",
                "mountains/macro/rocky_terrain_02/rocky_terrain_02_diff_1k.jpg",
                "mountains/macro/snow_field_aerial/snow_field_aerial_col_1k.jpg",
                "mountains/detail/dark_rock_02/dark_rock_02_diff_1k.jpg",
                "mountains/detail/rocks_ground_09/rocks_ground_09_diff_1k.jpg",
                "mountains/detail/lichen_rock/lichen_rock_diff_1k.jpg",
                "mountains/detail/snow_02/snow_02_diff_1k.jpg",
                "shared/detail/rocks_ground_02/rocks_ground_02_col_1k.jpg",
            ];
            Vector3[] paletteTargets =
            [
                new(96f, 103f, 76f),
                new(89f, 100f, 72f),
                new(72f, 72f, 58f),
                new(71f, 68f, 55f),
                new(97f, 101f, 94f),
                new(196f, 203f, 201f),
                new(91f, 94f, 94f),
                new(118f, 111f, 101f),
                new(91f, 102f, 84f),
                new(218f, 222f, 221f),
                new(105f, 91f, 75f),
            ];
            float[] repeatSizes = [78f, 2f, 3.15f, 2f, 90f, 80f, 2.001f, 3f, 2f, 2f, 2f];
            var images = relativePaths
                .Select(path => MaterialImage.Load(graphicsDevice, Path.Combine(root, path)))
                .ToArray();
            var random = new StableRandom(unchecked((ulong)(uint)seed) ^ 0xd28fb74391c65a0eUL);
            var layers = new MaterialLayer[images.Length];
            for (var index = 0; index < images.Length; index++)
            {
                layers[index] = new MaterialLayer(
                    images[index],
                    repeatSizes[index],
                    random.Range(-0.34f, 0.34f),
                    new Vector2(random.Range(-4f, 4f), random.Range(-4f, 4f)),
                    paletteTargets[index],
                    texelWorldSize);
            }

            return new MaterialSet(layers);
        }
    }

    private sealed class MaterialImage
    {
        private MaterialImage(MipLevel[] levels, Vector3 average)
        {
            Levels = levels;
            Average = average;
        }

        public int Width => Levels[0].Width;
        public MipLevel[] Levels { get; }
        public Vector3 Average { get; }

        public static MaterialImage Load(GraphicsDevice graphicsDevice, string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("A terrain material is missing from the deployed texture library.", path);
            }

            using var texture = Texture2D.FromFile(graphicsDevice, path);
            var pixels = new Color[texture.Width * texture.Height];
            texture.GetData(pixels);
            var red = 0d;
            var green = 0d;
            var blue = 0d;
            foreach (var color in pixels)
            {
                red += color.R;
                green += color.G;
                blue += color.B;
            }

            var count = pixels.Length;
            var levels = new List<MipLevel> { new(texture.Width, texture.Height, pixels) };
            while (levels[^1].Width > 1 || levels[^1].Height > 1)
            {
                var source = levels[^1];
                var width = Math.Max(1, source.Width / 2);
                var height = Math.Max(1, source.Height / 2);
                var downsampled = new Color[width * height];
                for (var row = 0; row < height; row++)
                {
                    for (var column = 0; column < width; column++)
                    {
                        var first = source.Pixels[(row * 2) * source.Width + column * 2];
                        var second = source.Pixels[(row * 2) * source.Width + Math.Min(source.Width - 1, column * 2 + 1)];
                        var third = source.Pixels[Math.Min(source.Height - 1, row * 2 + 1) * source.Width + column * 2];
                        var fourth = source.Pixels[Math.Min(source.Height - 1, row * 2 + 1) * source.Width + Math.Min(source.Width - 1, column * 2 + 1)];
                        downsampled[row * width + column] = new Color(
                            (byte)((first.R + second.R + third.R + fourth.R) / 4),
                            (byte)((first.G + second.G + third.G + fourth.G) / 4),
                            (byte)((first.B + second.B + third.B + fourth.B) / 4));
                    }
                }

                levels.Add(new MipLevel(width, height, downsampled));
            }

            return new MaterialImage(
                levels.ToArray(),
                new Vector3((float)(red / count), (float)(green / count), (float)(blue / count)));
        }

        public readonly record struct MipLevel(int Width, int Height, Color[] Pixels);
    }

    private readonly record struct MaterialLayer(
        MaterialImage Image,
        float RepeatSize,
        float Rotation,
        Vector2 Offset,
        Vector3 PaletteTarget,
        float TexelWorldSize)
    {
        public Color Sample(float worldX, float worldZ) => SampleProjected(worldX, worldZ);

        public Color Sample(float worldX, float worldY, float worldZ, Vector3 normal)
        {
            // Blend top and side projections on steep terrain so cliff materials do not smear
            // vertically. The two side projections share world Y through the terrain height.
            var topWeight = SmoothStep(0.20f, 0.72f, MathF.Abs(normal.Y));
            var sideAxisWeight = MathF.Abs(normal.X) /
                                 Math.Max(0.0001f, MathF.Abs(normal.X) + MathF.Abs(normal.Z));
            var sideX = SampleProjected(worldZ, worldY);
            var sideZ = SampleProjected(worldX, worldY);
            var side = Color.Lerp(sideZ, sideX, sideAxisWeight);
            return Color.Lerp(side, SampleProjected(worldX, worldZ), topWeight);
        }

        private Color SampleProjected(float first, float second)
        {
            var cosine = MathF.Cos(Rotation);
            var sine = MathF.Sin(Rotation);
            var u = (first * cosine - second * sine) / RepeatSize + Offset.X;
            var v = (first * sine + second * cosine) / RepeatSize + Offset.Y;
            var sourceFootprint = Math.Max(1f, TexelWorldSize / RepeatSize * Image.Width);
            var mipIndex = Math.Clamp(
                (int)MathF.Round(MathF.Log2(sourceFootprint)),
                0,
                Image.Levels.Length - 1);
            var mip = Image.Levels[mipIndex];
            // Four independently transformed neighbours overlap with smooth weights. This is a
            // compact stochastic-tiling bake: repetitions no longer line up into a visible grid,
            // while every sample remains seamless and deterministic for the world seed.
            var cellX = (int)MathF.Floor(u);
            var cellY = (int)MathF.Floor(v);
            var amountX = SmoothStep(0f, 1f, u - cellX);
            var amountY = SmoothStep(0f, 1f, v - cellY);
            var upper = Color.Lerp(
                SampleVariant(mip, u, v, cellX, cellY),
                SampleVariant(mip, u, v, cellX + 1, cellY),
                amountX);
            var lower = Color.Lerp(
                SampleVariant(mip, u, v, cellX, cellY + 1),
                SampleVariant(mip, u, v, cellX + 1, cellY + 1),
                amountX);
            var source = Color.Lerp(upper, lower, amountY);
            return new Color(
                Grade(source.R, Image.Average.X, PaletteTarget.X),
                Grade(source.G, Image.Average.Y, PaletteTarget.Y),
                Grade(source.B, Image.Average.Z, PaletteTarget.Z));
        }

        private static Color SampleVariant(
            MaterialImage.MipLevel mip,
            float u,
            float v,
            int cellX,
            int cellY)
        {
            var hash = Hash(cellX, cellY);
            var localU = u + ((hash & 1023u) / 1024f) * 0.83f;
            var localV = v + (((hash >> 10) & 1023u) / 1024f) * 0.83f;
            switch ((hash >> 20) & 3u)
            {
                case 1:
                    (localU, localV) = (-localV, localU);
                    break;
                case 2:
                    (localU, localV) = (-localU, -localV);
                    break;
                case 3:
                    (localU, localV) = (localV, -localU);
                    break;
            }

            localU -= MathF.Floor(localU);
            localV -= MathF.Floor(localV);
            var pixelX = localU * mip.Width - 0.5f;
            var pixelY = localV * mip.Height - 0.5f;
            var left = Wrap((int)MathF.Floor(pixelX), mip.Width);
            var right = Wrap(left + 1, mip.Width);
            var top = Wrap((int)MathF.Floor(pixelY), mip.Height);
            var bottom = Wrap(top + 1, mip.Height);
            var amountX = pixelX - MathF.Floor(pixelX);
            var amountY = pixelY - MathF.Floor(pixelY);
            var upper = Color.Lerp(mip.Pixels[top * mip.Width + left], mip.Pixels[top * mip.Width + right], amountX);
            var lower = Color.Lerp(mip.Pixels[bottom * mip.Width + left], mip.Pixels[bottom * mip.Width + right], amountX);
            return Color.Lerp(upper, lower, amountY);
        }

        private static uint Hash(int x, int y)
        {
            var value = unchecked((uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ 0xcb1ab31fu);
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            return value;
        }

        private static int Wrap(int value, int size) => (value % size + size) % size;

        private static byte Grade(byte value, float sourceAverage, float targetAverage) =>
            (byte)Math.Clamp((int)MathF.Round(value * targetAverage / Math.Max(1f, sourceAverage)), 0, 255);
    }
}
