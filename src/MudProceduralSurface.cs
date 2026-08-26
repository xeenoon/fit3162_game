using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Game;

/// <summary>
/// Generates a static mud material from random Voronoi-style sites. A graph walk joins neighboring
/// sites through unused paths into large concave regions, then repeated majority passes smooth the
/// extracted shared edges without introducing gaps. Region colours interlock in a fibrous blend.
/// </summary>
public sealed class MudProceduralSurface : IDisposable
{
    private const int FlowFrameCount = 12;
    private const float FlowCycleSeconds = 14f;
    private const float FlowUpdateInterval = 1f / 18f;

    private static readonly Vector3[] ReferencePalette =
    [
        new(43f, 26f, 16f),
        new(83f, 54f, 33f),
        new(114f, 78f, 51f),
        new(122f, 90f, 68f),
        new(134f, 91f, 58f),
        new(144f, 105f, 77f),
        new(169f, 136f, 108f),
        new(197f, 152f, 114f),
    ];

    private readonly record struct RegionStyle(Vector3 Albedo, float Wetness, float Height);
    private readonly record struct GraphPath(int From, int To);
    private sealed record BoundaryField(float[] Distances, int[] NeighbourRegions);
    private sealed record GeneratedMaterial(
        Color[] Pixels,
        float[] Wetness,
        float[] BoundaryDistances);

    private Texture2D? _texture;
    private Texture2D? _flowTexture;
    private Color[][] _flowFrames = [];
    private Color[] _flowPixels = [];
    private Rectangle _worldBounds;
    private float _lastFlowUpdate = float.NegativeInfinity;

    public void Configure(GraphicsDevice graphicsDevice, Rectangle worldBounds, int seed)
    {
        _texture?.Dispose();
        _flowTexture?.Dispose();
        _worldBounds = worldBounds;
        var width = Math.Clamp((int)MathF.Ceiling(worldBounds.Width / 2f), 200, 640);
        var height = Math.Clamp((int)MathF.Ceiling(worldBounds.Height / 2f), 150, 480);
        var material = GenerateMaterial(width, height, seed);
        _texture = new Texture2D(graphicsDevice, width, height, false, SurfaceFormat.Color);
        _texture.SetData(material.Pixels);
        _flowFrames = GenerateFlowFrames(
            width,
            height,
            seed,
            material.Wetness,
            material.BoundaryDistances);
        _flowPixels = new Color[width * height];
        _flowTexture = new Texture2D(graphicsDevice, width, height, false, SurfaceFormat.Color);
        _lastFlowUpdate = float.NegativeInfinity;
        Update(0f, force: true);
    }

    public void Update(float totalSeconds, bool force = false)
    {
        if (_flowTexture is null || (!force && totalSeconds - _lastFlowUpdate < FlowUpdateInterval))
        {
            return;
        }

        _lastFlowUpdate = totalSeconds;
        var position = totalSeconds / FlowCycleSeconds * FlowFrameCount;
        var firstFrame = (int)MathF.Floor(position) % FlowFrameCount;
        if (firstFrame < 0) firstFrame += FlowFrameCount;
        var secondFrame = (firstFrame + 1) % FlowFrameCount;
        var amount = position - MathF.Floor(position);
        amount = amount * amount * (3f - 2f * amount);
        var first = _flowFrames[firstFrame];
        var second = _flowFrames[secondFrame];
        for (var index = 0; index < _flowPixels.Length; index++)
        {
            _flowPixels[index] = new Color(
                (byte)MathHelper.Lerp(first[index].R, second[index].R, amount),
                (byte)MathHelper.Lerp(first[index].G, second[index].G, amount),
                (byte)MathHelper.Lerp(first[index].B, second[index].B, amount),
                (byte)MathHelper.Lerp(first[index].A, second[index].A, amount));
        }

        _flowTexture.SetData(_flowPixels);
    }

    public void Draw(PolygonRenderer renderer, IReadOnlyList<AreaPolygon> areas)
    {
        if (_texture is null) return;
        foreach (var area in areas)
        {
            renderer.DrawTexturedTriangles(area.FillTriangles, _texture, _worldBounds, Color.White);
        }

        if (_flowTexture is null) return;
        foreach (var area in areas)
        {
            renderer.DrawTexturedTriangles(area.FillTriangles, _flowTexture, _worldBounds, Color.White);
        }
    }

    public void DrawPreview(PolygonRenderer renderer)
    {
        if (_texture is null) return;
        var topLeft = new Vector2(_worldBounds.Left, _worldBounds.Top);
        var topRight = new Vector2(_worldBounds.Right, _worldBounds.Top);
        var bottomRight = new Vector2(_worldBounds.Right, _worldBounds.Bottom);
        var bottomLeft = new Vector2(_worldBounds.Left, _worldBounds.Bottom);
        renderer.DrawTexturedTriangles(
        [
            topLeft, topRight, bottomRight,
            topLeft, bottomRight, bottomLeft,
        ], _texture, _worldBounds, Color.White);
        if (_flowTexture is not null)
        {
            renderer.DrawTexturedTriangles(
            [
                topLeft, topRight, bottomRight,
                topLeft, bottomRight, bottomLeft,
            ], _flowTexture, _worldBounds, Color.White);
        }
    }

    private static GeneratedMaterial GenerateMaterial(int width, int height, int seed)
    {
        var random = new Random(seed ^ 0x4d35a);
        var noise = new PerlinNoise(seed ^ 0x51f15e);
        var sites = CreateSites(width, height, random);
        var siteMap = BuildVoronoiMap(width, height, sites);
        var adjacency = BuildAdjacency(siteMap, width, height, sites.Count);
        var siteRegions = WalkUnusedPaths(adjacency, random, out var regionCount);
        var regionMap = new int[siteMap.Length];
        for (var index = 0; index < siteMap.Length; index++)
        {
            regionMap[index] = siteRegions[siteMap[index]];
        }

        // These passes are the edge-smoothing stage after the graph-grown Voronoi regions exist.
        regionMap = SmoothRegionEdges(regionMap, width, height, passes: 16);
        var boundary = ExtractBoundaryField(regionMap, width, height, maximumDistance: 30);
        var styles = CreateRegionStyles(regionCount, random);
        var sampleCount = width * height;
        var albedo = new Vector3[sampleCount];
        var heights = new float[sampleCount];
        var wetness = new float[sampleCount];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var region = regionMap[index];
                var style = styles[region];
                var colour = style.Albedo;
                var materialWetness = style.Wetness;
                var plateHeight = style.Height;
                var neighbour = boundary.NeighbourRegions[index];
                var edgeDistance = boundary.Distances[index];

                if (neighbour >= 0 && neighbour != region && edgeDistance <= 30f)
                {
                    var blendWidth = 18f + Hash01(region, neighbour, seed) * 10f;
                    if (edgeDistance < blendWidth)
                    {
                        var proximity = 1f - edgeDistance / blendWidth;
                        var horizontalFibres = noise.Fractal(
                            x * 0.19f + 17.3f,
                            y * 0.064f - 28.7f,
                            octaves: 2,
                            persistence: 0.46f);
                        var verticalFibres = noise.Fractal(
                            x * 0.066f - 31.4f,
                            y * 0.2f + 9.8f,
                            octaves: 2,
                            persistence: 0.46f);
                        var crossThreads = MathF.Sin(x * 0.61f + horizontalFibres * 6.1f) *
                            MathF.Sin(y * 0.53f - verticalFibres * 5.7f) * 0.5f + 0.5f;
                        var mesh = horizontalFibres * 0.37f + verticalFibres * 0.37f +
                            crossThreads * 0.26f;
                        var smoothMix = SmoothStep(0f, 1f, proximity) * 0.5f;
                        var meshFade = SmoothStep(0f, 0.24f, proximity) *
                            (1f - SmoothStep(0.78f, 1f, proximity));
                        var colourMix = MathHelper.Clamp(
                            smoothMix + (mesh - 0.5f) * 0.16f * meshFade,
                            0f,
                            0.5f);
                        var neighbourStyle = styles[neighbour];
                        colour = Vector3.Lerp(colour, neighbourStyle.Albedo, colourMix);
                        materialWetness = MathHelper.Lerp(
                            materialWetness,
                            neighbourStyle.Wetness,
                            smoothMix);
                        plateHeight = MathHelper.Lerp(
                            plateHeight,
                            neighbourStyle.Height,
                            smoothMix);
                    }
                }

                var undulation = noise.Fractal(x * 0.023f, y * 0.023f, octaves: 3);
                var clayLumps = FractalValueNoise(
                    x * 0.078f + 21.8f,
                    y * 0.078f - 13.2f,
                    seed ^ 0x35a1,
                    octaves: 3);
                var dryClayRelief = FractalValueNoise(
                    x * 0.048f - 11.6f,
                    y * 0.048f + 32.9f,
                    seed ^ 0x45c7,
                    octaves: 2);
                var fineRelief = FractalValueNoise(
                    x * 0.24f - 37.1f,
                    y * 0.24f + 54.6f,
                    seed ^ 0x719d,
                    octaves: 2);
                materialWetness = MathHelper.Clamp(
                    materialWetness + (undulation - 0.5f) * 0.2f,
                    0.06f,
                    0.96f);
                var dryAmount = 1f - materialWetness;

                var colourVariation = (clayLumps - 0.5f) * MathHelper.Lerp(2.5f, 14f, dryAmount);
                colour += new Vector3(
                    colourVariation,
                    colourVariation * 0.78f,
                    colourVariation * 0.61f);

                var sampleHeight = plateHeight + (undulation - 0.5f) * 3.6f +
                    (clayLumps - 0.5f) * 0.15f +
                    (dryClayRelief - 0.5f) * 5.55f * dryAmount +
                    (fineRelief - 0.5f) * MathHelper.Lerp(0.01f, 0.4f, dryAmount);

                albedo[index] = colour;
                heights[index] = sampleHeight;
                wetness[index] = materialWetness;
            }
        }

        return new GeneratedMaterial(
            LightHeightField(albedo, heights, wetness, width, height, seed),
            wetness,
            boundary.Distances);
    }

    private static Color[] LightHeightField(
        IReadOnlyList<Vector3> albedo,
        IReadOnlyList<float> heights,
        IReadOnlyList<float> wetness,
        int width,
        int height,
        int seed)
    {
        var pixels = new Color[width * height];
        var lightDirection = Vector3.Normalize(new Vector3(-0.52f, -0.67f, 1f));
        var halfDirection = Vector3.Normalize(lightDirection + Vector3.UnitZ);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var left = heights[y * width + Math.Max(0, x - 1)];
                var right = heights[y * width + Math.Min(width - 1, x + 1)];
                var top = heights[Math.Max(0, y - 1) * width + x];
                var bottom = heights[Math.Min(height - 1, y + 1) * width + x];
                var farLeft = heights[y * width + Math.Max(0, x - 4)];
                var farRight = heights[y * width + Math.Min(width - 1, x + 4)];
                var farTop = heights[Math.Max(0, y - 4) * width + x];
                var farBottom = heights[Math.Min(height - 1, y + 4) * width + x];
                var materialWetness = wetness[index];
                var microStrength = 1f - materialWetness * 0.99f;
                var macroStrength = 1.1f - materialWetness * 0.25f;
                var normalX = (left - right) * microStrength * 1.3f +
                    (farLeft - farRight) * 0.25f * macroStrength;
                var normalY = (top - bottom) * microStrength * 1.3f +
                    (farTop - farBottom) * 0.25f * macroStrength;
                var normal = Vector3.Normalize(new Vector3(normalX, normalY, 1.08f));
                var diffuse = MathF.Max(0f, Vector3.Dot(normal, lightDirection));
                var lighting = 0.48f + diffuse * 0.61f;
                var normalHalf = MathF.Max(0f, Vector3.Dot(normal, halfDirection));
                var wetSpecularMask = SmoothStep(0.5f, 0.86f, materialWetness);
                var gloss = 0.008f + wetSpecularMask * 1.28f;
                var broadSheen = MathF.Pow(normalHalf, 10f) * gloss * 9f;
                var sharpSpecular = MathF.Pow(normalHalf, 62f) * gloss * 118f;
                var colour = albedo[index] * lighting;
                var reflectedMudLight = ReferencePalette[7];
                var reflectionAmount = wetSpecularMask * 0.035f +
                    MathF.Pow(normalHalf, 13f) * wetSpecularMask * 0.12f;
                colour = Vector3.Lerp(colour, reflectedMudLight, reflectionAmount);
                var cavity = MathF.Max(0f, (left + right + top + bottom) * 0.25f - heights[index]);
                colour -= new Vector3(cavity * 5.8f, cavity * 5f, cavity * 4.2f);
                colour += new Vector3(
                    broadSheen + sharpSpecular,
                    broadSheen * 0.91f + sharpSpecular * 0.88f,
                    broadSheen * 0.78f + sharpSpecular * 0.72f);
                pixels[index] = new Color(
                    ClampByte(colour.X),
                    ClampByte(colour.Y),
                    ClampByte(colour.Z),
                    (byte)255);
            }
        }

        return pixels;
    }

    private static Color[][] GenerateFlowFrames(
        int width,
        int height,
        int seed,
        IReadOnlyList<float> wetness,
        IReadOnlyList<float> boundaryDistances)
    {
        var frames = new Color[FlowFrameCount][];
        for (var frameIndex = 0; frameIndex < FlowFrameCount; frameIndex++)
        {
            var phase = MathHelper.TwoPi * frameIndex / FlowFrameCount;
            var phaseSine = MathF.Sin(phase);
            var phaseCosine = MathF.Cos(phase);
            var frame = new Color[width * height];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    var wetMask = SmoothStep(0.55f, 0.9f, wetness[index]);
                    if (wetMask <= 0f)
                    {
                        frame[index] = Color.Transparent;
                        continue;
                    }

                    wetMask *= wetMask;
                    var edgeMask = 1f - SmoothStep(3f, 17f, boundaryDistances[index]);
                    var flowMask = wetMask * (0.008f + edgeMask * 0.86f);
                    var directionField = ValueNoise(
                        x * 0.011f + phaseCosine * 0.42f,
                        y * 0.011f + phaseSine * 0.42f,
                        seed ^ 0x2fa7);
                    var angle = 0.46f + phaseSine * 0.14f + (directionField - 0.5f) * 0.72f;
                    var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    var along = x * direction.X + y * direction.Y;
                    var across = -x * direction.Y + y * direction.X;
                    var warp = FractalValueNoise(
                        x * 0.027f + phaseCosine * 0.28f,
                        y * 0.027f + phaseSine * 0.28f,
                        seed ^ 0x6d91,
                        octaves: 2);

                    var broadWave = MathF.Sin(along * 0.061f - phase + warp * 4.2f) * 0.5f + 0.5f;
                    var middleWave = MathF.Sin(
                        along * 0.113f + across * 0.009f - phase * 2f + warp * 3.4f) * 0.5f + 0.5f;
                    var fineWave = MathF.Sin(
                        along * 0.19f - across * 0.014f - phase * 3f + warp * 2.7f) * 0.5f + 0.5f;
                    var broadLayer = SmoothStep(0.6f, 0.9f, broadWave) * 0.55f;
                    var middleLayer = SmoothStep(0.68f, 0.92f, middleWave) * 0.3f;
                    var fineLayer = SmoothStep(0.76f, 0.95f, fineWave) * 0.15f;
                    var layeredFlow = broadLayer + middleLayer + fineLayer;
                    var alpha = ClampByte(layeredFlow * flowMask * 54f);
                    frame[index] = Color.FromNonPremultiplied(190, 147, 105, alpha);
                }
            }

            frames[frameIndex] = frame;
        }

        return frames;
    }

    private static List<Vector2> CreateSites(int width, int height, Random random)
    {
        var targetCount = Math.Clamp(width * height / 2400, 28, 105);
        var minimumDistance = MathF.Sqrt(width * height / (float)targetCount) * 0.52f;
        var minimumDistanceSquared = minimumDistance * minimumDistance;
        var sites = new List<Vector2>(targetCount);
        for (var attempts = 0; attempts < targetCount * 100 && sites.Count < targetCount; attempts++)
        {
            var candidate = new Vector2(
                2f + (float)random.NextDouble() * Math.Max(1f, width - 4f),
                2f + (float)random.NextDouble() * Math.Max(1f, height - 4f));
            var accepted = true;
            foreach (var site in sites)
            {
                if (Vector2.DistanceSquared(candidate, site) < minimumDistanceSquared)
                {
                    accepted = false;
                    break;
                }
            }

            if (accepted) sites.Add(candidate);
        }

        return sites;
    }

    private static int[] BuildVoronoiMap(int width, int height, IReadOnlyList<Vector2> sites)
    {
        var map = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var point = new Vector2(x + 0.5f, y + 0.5f);
                var nearest = 0;
                var nearestDistance = float.MaxValue;
                for (var siteIndex = 0; siteIndex < sites.Count; siteIndex++)
                {
                    var distance = Vector2.DistanceSquared(point, sites[siteIndex]);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = siteIndex;
                    }
                }

                map[y * width + x] = nearest;
            }
        }

        return map;
    }

    private static List<HashSet<int>> BuildAdjacency(
        IReadOnlyList<int> siteMap,
        int width,
        int height,
        int siteCount)
    {
        var adjacency = new List<HashSet<int>>(siteCount);
        for (var index = 0; index < siteCount; index++) adjacency.Add([]);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var current = siteMap[y * width + x];
                if (x + 1 < width) Connect(current, siteMap[y * width + x + 1]);
                if (y + 1 < height) Connect(current, siteMap[(y + 1) * width + x]);
            }
        }

        return adjacency;

        void Connect(int first, int second)
        {
            if (first == second) return;
            adjacency[first].Add(second);
            adjacency[second].Add(first);
        }
    }

    private static int[] WalkUnusedPaths(
        IReadOnlyList<HashSet<int>> adjacency,
        Random random,
        out int regionCount)
    {
        var regions = new int[adjacency.Count];
        Array.Fill(regions, -1);
        var usedPaths = new HashSet<long>();
        var unclaimed = adjacency.Count;
        regionCount = 0;

        while (unclaimed > 0)
        {
            var start = PickUnclaimed(regions, random);
            var availableFromStart = UnclaimedPathsFrom(start, adjacency, regions, usedPaths);
            if (availableFromStart.Count == 0)
            {
                var assignedNeighbour = FirstAssignedNeighbour(start, adjacency, regions);
                if (assignedNeighbour >= 0)
                {
                    regions[start] = assignedNeighbour;
                    unclaimed--;
                    continue;
                }
            }

            var claimedSites = new List<int> { start };
            regions[start] = regionCount;
            unclaimed--;
            var steps = random.Next(4, 12);

            for (var step = 1; step < steps; step++)
            {
                var candidates = new List<GraphPath>();
                foreach (var existingPoint in claimedSites)
                {
                    candidates.AddRange(UnclaimedPathsFrom(existingPoint, adjacency, regions, usedPaths));
                }

                if (candidates.Count == 0) break;
                var path = candidates[random.Next(candidates.Count)];
                usedPaths.Add(PathKey(path.From, path.To));
                if (regions[path.To] >= 0)
                {
                    step--;
                    continue;
                }

                regions[path.To] = regionCount;
                claimedSites.Add(path.To);
                unclaimed--;
            }

            regionCount++;
        }

        return regions;
    }

    private static List<GraphPath> UnclaimedPathsFrom(
        int from,
        IReadOnlyList<HashSet<int>> adjacency,
        IReadOnlyList<int> regions,
        IReadOnlySet<long> usedPaths)
    {
        var paths = new List<GraphPath>();
        foreach (var to in adjacency[from])
        {
            if (regions[to] < 0 && !usedPaths.Contains(PathKey(from, to)))
            {
                paths.Add(new GraphPath(from, to));
            }
        }

        return paths;
    }

    private static int PickUnclaimed(IReadOnlyList<int> regions, Random random)
    {
        var start = random.Next(regions.Count);
        for (var offset = 0; offset < regions.Count; offset++)
        {
            var index = (start + offset) % regions.Count;
            if (regions[index] < 0) return index;
        }

        return 0;
    }

    private static int FirstAssignedNeighbour(
        int site,
        IReadOnlyList<HashSet<int>> adjacency,
        IReadOnlyList<int> regions)
    {
        foreach (var neighbour in adjacency[site])
        {
            if (regions[neighbour] >= 0) return regions[neighbour];
        }

        return -1;
    }

    private static long PathKey(int first, int second)
    {
        var low = Math.Min(first, second);
        var high = Math.Max(first, second);
        return ((long)low << 32) | (uint)high;
    }

    private static int[] SmoothRegionEdges(int[] source, int width, int height, int passes)
    {
        var current = source;
        for (var pass = 0; pass < passes; pass++)
        {
            var next = (int[])current.Clone();
            for (var y = 1; y < height - 1; y++)
            {
                for (var x = 1; x < width - 1; x++)
                {
                    var counts = new Dictionary<int, int>();
                    for (var offsetY = -1; offsetY <= 1; offsetY++)
                    {
                        for (var offsetX = -1; offsetX <= 1; offsetX++)
                        {
                            var label = current[(y + offsetY) * width + x + offsetX];
                            counts[label] = counts.GetValueOrDefault(label) + 1;
                        }
                    }

                    var bestLabel = current[y * width + x];
                    var bestCount = counts[bestLabel];
                    foreach (var pair in counts)
                    {
                        if (pair.Value > bestCount)
                        {
                            bestLabel = pair.Key;
                            bestCount = pair.Value;
                        }
                    }

                    if (bestCount >= 5) next[y * width + x] = bestLabel;
                }
            }

            current = next;
        }

        return current;
    }

    private static BoundaryField ExtractBoundaryField(
        IReadOnlyList<int> regions,
        int width,
        int height,
        int maximumDistance)
    {
        var distances = new float[regions.Count];
        var neighbours = new int[regions.Count];
        Array.Fill(distances, float.MaxValue);
        Array.Fill(neighbours, -1);
        var queue = new Queue<int>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var ownRegion = regions[index];
                var otherRegion = DifferentNeighbour(x, y, ownRegion, regions, width, height);
                if (otherRegion >= 0)
                {
                    distances[index] = 0f;
                    neighbours[index] = otherRegion;
                    queue.Enqueue(index);
                }
            }
        }

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            if (distances[index] >= maximumDistance) continue;
            var x = index % width;
            var y = index / width;
            TryPropagate(x - 1, y, index);
            TryPropagate(x + 1, y, index);
            TryPropagate(x, y - 1, index);
            TryPropagate(x, y + 1, index);
        }

        return new BoundaryField(distances, neighbours);

        void TryPropagate(int x, int y, int fromIndex)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return;
            var toIndex = y * width + x;
            if (regions[toIndex] != regions[fromIndex]) return;
            var distance = distances[fromIndex] + 1f;
            if (distance >= distances[toIndex] || distance > maximumDistance) return;
            distances[toIndex] = distance;
            neighbours[toIndex] = neighbours[fromIndex];
            queue.Enqueue(toIndex);
        }
    }

    private static int DifferentNeighbour(
        int x,
        int y,
        int ownRegion,
        IReadOnlyList<int> regions,
        int width,
        int height)
    {
        if (x > 0 && regions[y * width + x - 1] != ownRegion) return regions[y * width + x - 1];
        if (x + 1 < width && regions[y * width + x + 1] != ownRegion) return regions[y * width + x + 1];
        if (y > 0 && regions[(y - 1) * width + x] != ownRegion) return regions[(y - 1) * width + x];
        if (y + 1 < height && regions[(y + 1) * width + x] != ownRegion) return regions[(y + 1) * width + x];
        return -1;
    }

    private static RegionStyle[] CreateRegionStyles(int count, Random random)
    {
        var styles = new RegionStyle[count];
        for (var index = 0; index < count; index++)
        {
            var regionWetness = random.NextDouble() < 0.62
                ? 0.78f + (float)random.NextDouble() * 0.2f
                : 0.1f + (float)random.NextDouble() * 0.38f;
            var paletteAmount = 0.1f + (1f - regionWetness) * 0.82f +
                ((float)random.NextDouble() * 2f - 1f) * 0.07f;
            var reference = Vector3.Lerp(
                ReferencePalette[1],
                ReferencePalette[7],
                MathHelper.Clamp(paletteAmount, 0f, 1f));
            var jitter = ((float)random.NextDouble() * 2f - 1f) * 3f;
            var albedo = reference + new Vector3(jitter, jitter * 0.75f, jitter * 0.55f);
            var regionHeight = (0.5f - regionWetness) * 4.2f +
                ((float)random.NextDouble() * 2f - 1f) * 0.75f;
            styles[index] = new RegionStyle(albedo, regionWetness, regionHeight);
        }

        return styles;
    }

    private static float Hash01(int first, int second, int seed)
    {
        unchecked
        {
            var value = (uint)Math.Min(first, second) * 0x8da6b343u ^
                (uint)Math.Max(first, second) * 0xd8163841u ^ (uint)seed;
            value ^= value >> 16;
            value *= 0x7feb352du;
            return (value & 0x00ffffffu) / 16777215f;
        }
    }

    private static float PixelHash01(int x, int y, int seed)
    {
        unchecked
        {
            var value = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)seed;
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            return (value & 0x00ffffffu) / 16777215f;
        }
    }

    private static float FractalValueNoise(float x, float y, int seed, int octaves)
    {
        var value = 0f;
        var amplitude = 1f;
        var amplitudeTotal = 0f;
        for (var octave = 0; octave < octaves; octave++)
        {
            value += ValueNoise(x, y, seed + octave * 1013) * amplitude;
            amplitudeTotal += amplitude;
            x *= 2f;
            y *= 2f;
            amplitude *= 0.5f;
        }

        return value / amplitudeTotal;
    }

    private static float ValueNoise(float x, float y, int seed)
    {
        var left = (int)MathF.Floor(x);
        var top = (int)MathF.Floor(y);
        var amountX = SmoothCurve(x - left);
        var amountY = SmoothCurve(y - top);
        var topValue = MathHelper.Lerp(
            PixelHash01(left, top, seed),
            PixelHash01(left + 1, top, seed),
            amountX);
        var bottomValue = MathHelper.Lerp(
            PixelHash01(left, top + 1, seed),
            PixelHash01(left + 1, top + 1, seed),
            amountX);
        return MathHelper.Lerp(topValue, bottomValue, amountY);
    }

    private static float SmoothCurve(float value) =>
        value * value * value * (value * (value * 6f - 15f) + 10f);

    private static float SmoothStep(float from, float to, float value)
    {
        var amount = MathHelper.Clamp((value - from) / (to - from), 0f, 1f);
        return amount * amount * (3f - 2f * amount);
    }

    private static byte ClampByte(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);

    public void Dispose()
    {
        _texture?.Dispose();
        _flowTexture?.Dispose();
        _texture = null;
        _flowTexture = null;
    }
}
