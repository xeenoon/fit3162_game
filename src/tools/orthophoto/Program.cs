using System.IO.Compression;
using Game;
using Microsoft.Xna.Framework;

// EarthGen-style orthophoto proof.
//
// This is an OFFLINE, CPU-only bake. It generates the exact same world the game generates
// (WorldGenerator.Generate), then composites a high-resolution top-down "satellite" image from the
// world's control fields — biome/forest/scree/snow masks, hydrology, and the REAL surface normals.
//
// The point of the proof: prove the EarthGen macro-albedo look (multi-scale color + landcover texture
// + correct relief) is achievable from the data we already generate, before touching the 3D renderer.

var seed = 2026;
var resolution = 2048;
var worldScale = 1f;
var outPath = Path.Combine(AppContext.BaseDirectory, "orthophoto.png");

for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--seed": seed = int.Parse(args[i + 1]); break;
        case "--size": resolution = int.Parse(args[i + 1]); break;
        case "--scale": worldScale = float.Parse(args[i + 1]); break;
        case "--out": outPath = Path.GetFullPath(args[i + 1]); break;
    }
}

Console.WriteLine($"Generating world seed={seed} scale={worldScale} ...");
var world = WorldGenerator.Generate(seed, worldScale: worldScale);
Console.WriteLine($"Baking {resolution}x{resolution} orthophoto ...");

var pixels = Orthophoto.Bake(world, resolution);
Png.Write(outPath, resolution, resolution, pixels);
Console.WriteLine($"Wrote {outPath}");

// ---------------------------------------------------------------------------------------------

static class Orthophoto
{
    // Sun for the analytic hillshade. Real normals mean this relief is physically consistent with the
    // playable heightfield — unlike EarthGen, which infers depth from a generated image.
    static readonly Vector3 Sun = Vector3.Normalize(new Vector3(-0.55f, 0.86f, -0.35f));

    public static byte[] Bake(GeneratedWorld world, int size)
    {
        var terrain = world.Terrain;
        var half = terrain.Settings.Size * 0.5f;
        var pixels = new byte[size * size * 3];

        var seed = (uint)world.Seed;
        var macroNoise = new Noise(seed ^ 0x9ce7431bu);
        var moistNoise = new Noise(seed ^ 0x6a51bf20u);
        var canopyNoise = new Noise(seed ^ 0x1b873593u);
        var grainNoise = new Noise(seed ^ 0x27d4eb2fu);
        var detailNoise = new Noise(seed ^ 0x85ebca6bu);

        // Precompute lake ellipses for fast per-pixel water test.
        var lakes = world.Lakes
            .Select(l => (C: l.Center, R: l.Radius))
            .ToArray();

        Parallel.For(0, size, row =>
        {
            var worldZ = MathHelper.Lerp(-half, half, (row + 0.5f) / size);
            for (var col = 0; col < size; col++)
            {
                var worldX = MathHelper.Lerp(-half, half, (col + 0.5f) / size);

                var normal = terrain.SampleNormal(worldX, worldZ);
                var slope = MathHelper.ToDegrees(MathF.Acos(Clamp(normal.Y, -1f, 1f)));
                var nHeight = terrain.NormalizedHeight(terrain.SampleHeight(worldX, worldZ));
                var mountain = terrain.SampleMountainInfluence(worldX, worldZ);

                var forest = terrain.SampleForestMask(worldX, worldZ);
                var scree = terrain.SampleScreeMask(worldX, worldZ);
                var screeDep = terrain.SampleScreeDeposit(worldX, worldZ);
                var snow = terrain.SampleSnowMask(worldX, worldZ);
                var rock = terrain.SampleRockMask(worldX, worldZ);
                var strata = terrain.SampleStrataMask(worldX, worldZ);
                var clearing = terrain.SampleClearingMask(worldX, worldZ);
                var wet = terrain.SampleWetValleyMask(worldX, worldZ);
                var flow = terrain.SampleFlowAccumulation(worldX, worldZ);
                var drainDist = terrain.SampleDistanceToDrainage(worldX, worldZ);
                var ao = terrain.SampleAmbientAccessibility(worldX, worldZ);
                var sunVis = terrain.SampleSunVisibility(worldX, worldZ);

                // --- Multi-scale color: macro tint over hundreds of metres so nothing reads uniform.
                var macroA = macroNoise.Fbm(worldX * 0.010f, worldZ * 0.010f, 2);
                var macroB = moistNoise.Fbm(worldX * 0.0075f + 17f, worldZ * 0.0075f - 29f, 2);
                var moisture = moistNoise.Fbm(worldX * 0.021f, worldZ * 0.021f, 3);
                var boundary = macroNoise.Fbm(worldX * 0.033f, worldZ * 0.033f, 3);

                // ============================ LOWLAND / HILLS ALBEDO ============================
                // Palette authored directly in sRGB display space; deeper/more saturated than v1 so
                // the lowlands don't wash out to milky sage.
                var grass = Mix(new Vector3(74, 92, 48), new Vector3(104, 116, 60), macroA);
                grass *= 0.9f + moistNoise.Fbm(worldX * 0.28f, worldZ * 0.28f, 2) * 0.22f; // meadow mottle
                var dry = SmoothStep(0.50f, 0.72f, macroA + nHeight * 0.18f - moisture * 0.12f);
                var damp = Clamp(SmoothStep(0.52f, 0.72f, macroB) * (1f - nHeight) * 0.6f, 0f, 0.85f);
                var scrub = SmoothStep(0.48f, 0.68f, macroA * 0.55f + boundary * 0.45f) * (1f - damp);
                var hills = grass;
                hills = Mix(hills, new Vector3(146, 126, 74), dry * 0.5f);        // dry tan ridges
                hills = Mix(hills, new Vector3(48, 66, 48), damp * 0.55f);        // damp basins
                hills = Mix(hills, new Vector3(84, 92, 50), scrub * 0.35f);       // olive scrub
                hills = Mix(hills, new Vector3(158, 138, 84), clearing * 0.55f);  // bare clearings
                hills = Mix(hills, new Vector3(40, 58, 44), wet * 0.6f);          // wet corridor

                // Forest as a CANOPY TEXTURE, not flat green: mid-freq clump structure + fine grain.
                // This is the single biggest difference from the game's flat-cone look. Made dark and
                // dominant so forest masses read clearly against grass instead of blending away.
                var clump = canopyNoise.Fbm(worldX * 0.12f, worldZ * 0.12f, 4);
                var canopyGrain = canopyNoise.Fbm(worldX * 0.6f, worldZ * 0.6f, 2);
                var canopyLit = 0.82f + (clump - 0.5f) * 0.85f + (canopyGrain - 0.5f) * 0.45f;
                var canopy = new Vector3(30, 44, 26) * Clamp(canopyLit, 0.6f, 1.5f);
                canopy = Mix(canopy, new Vector3(48, 64, 34), SmoothStep(0.55f, 0.9f, clump)); // sunlit crowns
                // Feather the treeline with the canopy clump field so forest edges break up organically
                // instead of scalloping along the low-res mask contour.
                var forestWeight = SmoothStep(0.30f, 0.62f, forest + (clump - 0.5f) * 0.22f) *
                                   (1f - SmoothStep(22f, 36f, slope));
                hills = Mix(hills, canopy, forestWeight);

                // ============================ MOUNTAIN ALBEDO ============================
                // Rock carries real hue: warm ochre stone shifting cool/blue with strata, not flat gray.
                var rockBase = Mix(new Vector3(128, 118, 104), new Vector3(92, 96, 104), strata);
                var mtn = rockBase;
                mtn = Mix(mtn, new Vector3(156, 146, 128), Clamp(scree * 0.85f + screeDep * 0.4f, 0f, 0.85f)); // pale scree
                var mtnDrain = 1f - SmoothStep(0.5f, 4f, drainDist);
                mtn = Mix(mtn, new Vector3(70, 74, 74), mtnDrain * 0.55f);        // damp gully rock
                // Subtle rock grain only (no salt-and-pepper): low-amplitude, broad.
                var rockGrain = grainNoise.Fbm(worldX * 0.35f, worldZ * 0.35f, 3);
                mtn *= 0.93f + (rockGrain - 0.5f) * 0.22f;
                // Lichen/veg greening on lower, moister, gentler slopes.
                var lichen = SmoothStep(0.5f, 0.72f, moisture) * (1f - SmoothStep(0.6f, 0.85f, nHeight)) *
                             (1f - SmoothStep(26f, 40f, slope));
                mtn = Mix(mtn, new Vector3(84, 92, 66), lichen * 0.4f);
                // Sparse alpine conifer floor bleeding up from the treeline.
                var alpineForest = SmoothStep(0.34f, 0.58f, forest) * mountain * (1f - SmoothStep(24f, 36f, slope));
                mtn = Mix(mtn, new Vector3(32, 46, 30), alpineForest * 0.6f);

                var albedo = Mix(hills, mtn, SmoothStep(0.18f, 0.6f, mountain + (boundary - 0.5f) * 0.16f));

                // Snow last (it sits on top of everything, softened by a boundary break-up).
                var snowCover = Clamp(snow - (boundary - 0.5f) * 0.4f, 0f, 1f) * SmoothStep(0f, 25f, 40f - slope);
                albedo = Mix(albedo, new Vector3(206, 213, 221), snowCover);

                // --- Hydrology tint: wet ground is darker & desaturated; strong flow reads as stream.
                var wetness = Clamp(wet * 0.6f + (1f - SmoothStep(1.5f, 8f, drainDist)) * 0.5f, 0f, 0.8f);
                albedo = Desaturate(albedo * (1f - wetness * 0.24f), wetness * 0.3f);
                var streamPix = (1f - SmoothStep(0.5f, 2.5f, drainDist)) * SmoothStep(6f, 50f, flow);
                albedo = Mix(albedo, new Vector3(58, 76, 82), streamPix * 0.7f);

                // ---------------------------- RELIEF (correct hillshade) ----------------------------
                // Synthesize fine relief the 193x193 heightfield lacks (EarthGen's core idea: invent
                // plausible high-frequency detail that the coarse layer doesn't contain). We perturb the
                // shading normal with the gradient of a detail fbm, weighted toward rock/scree/steep
                // ground so lowland meadows stay smooth.
                var detailWeight = Clamp(0.18f + rock * 0.9f + scree * 0.7f + mountain * 0.5f +
                                         SmoothStep(6f, 26f, slope) * 0.6f, 0f, 1.7f);
                const float eps = 0.4f;
                float DetailH(float x, float z) =>
                    detailNoise.Fbm(x * 0.55f, z * 0.55f, 4) * 0.7f + detailNoise.Fbm(x * 1.7f, z * 1.7f, 3) * 0.3f;
                var gx = (DetailH(worldX + eps, worldZ) - DetailH(worldX - eps, worldZ)) / (2f * eps);
                var gz = (DetailH(worldX, worldZ + eps) - DetailH(worldX, worldZ - eps)) / (2f * eps);
                var shadeNormal = Vector3.Normalize(normal + new Vector3(-gx, 0f, -gz) * (3.2f * detailWeight));

                // Directional key + hemispheric fill, using real normals, cast-shadow visibility and AO.
                // Tuned so flat, fully-lit ground ~= authored albedo (shade ~1.0), shadow floor ~0.35.
                var key = MathF.Pow(MathF.Max(0f, Vector3.Dot(shadeNormal, Sun)), 0.8f);
                var sky = 0.5f + 0.5f * normal.Y;
                var ambient = 0.30f + 0.22f * ao * sky;
                var direct = key * (0.25f + 0.75f * sunVis);
                var shade = ambient + 0.62f * direct;        // ~1.0 on lit flat ground, up to ~1.15 on sun slopes
                albedo *= shade;

                // --- Water: lakes as depth-graded ellipses.
                foreach (var (c, r) in lakes)
                {
                    var e = ((worldX - c.X) * (worldX - c.X)) / (r.X * r.X)
                          + ((worldZ - c.Y) * (worldZ - c.Y)) / (r.Y * r.Y);
                    if (e < 1.15f)
                    {
                        var depth = SmoothStep(1.15f, 0.2f, e);
                        var waterCol = Mix(new Vector3(74, 104, 108), new Vector3(34, 56, 68), depth);
                        albedo = Mix(albedo, waterCol, Clamp(depth * 1.3f, 0f, 1f));
                    }
                }

                // --- Tone: slight S-curve contrast + saturation. No filmic/gamma: palette is already
                // authored in display space, so we keep the colors we picked.
                var c0 = albedo / 255f;
                c0 = Saturate(c0, 1.12f);
                c0 = new Vector3(Contrast(c0.X), Contrast(c0.Y), Contrast(c0.Z));
                var idx = (row * size + col) * 3;
                pixels[idx + 0] = ToByte(c0.X);
                pixels[idx + 1] = ToByte(c0.Y);
                pixels[idx + 2] = ToByte(c0.Z);
            }
        });

        return pixels;
    }

    static Vector3 Mix(Vector3 a, Vector3 b, float t) => Vector3.Lerp(a, b, Clamp(t, 0f, 1f));
    static float Clamp(float v, float lo, float hi) => MathHelper.Clamp(v, lo, hi);

    static float SmoothStep(float e0, float e1, float x)
    {
        var t = Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    static Vector3 Desaturate(Vector3 c, float amount)
    {
        var g = c.X * 0.299f + c.Y * 0.587f + c.Z * 0.114f;
        return Vector3.Lerp(c, new Vector3(g), Clamp(amount, 0f, 1f));
    }

    static Vector3 Saturate(Vector3 c, float amount)
    {
        var g = c.X * 0.299f + c.Y * 0.587f + c.Z * 0.114f;
        return new Vector3(g) + (c - new Vector3(g)) * amount;
    }

    // Gentle S-curve around mid-grey: deepens shadows, keeps highlights, adds punch without crushing.
    static float Contrast(float v)
    {
        v = Clamp(v, 0f, 1f);
        return Clamp(0.5f + (v - 0.5f) * 1.14f + (v - 0.5f) * (v - 0.5f) * (v - 0.5f) * 0.6f, 0f, 1f);
    }

    static byte ToByte(float v) => (byte)Clamp(Clamp(v, 0f, 1f) * 255f, 0f, 255f);
}

// Small hash-based value-noise fbm. Deterministic per seed; independent of world coordinate origin.
sealed class Noise
{
    readonly uint _seed;
    public Noise(uint seed) => _seed = seed;

    static float Hash(int x, int y, uint seed)
    {
        var h = (uint)(x * 374761393) ^ (uint)(y * 668265263) ^ seed;
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    float Value(float x, float y)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        var a = Hash(xi, yi, _seed);
        var b = Hash(xi + 1, yi, _seed);
        var c = Hash(xi, yi + 1, _seed);
        var d = Hash(xi + 1, yi + 1, _seed);
        return MathHelper.Lerp(MathHelper.Lerp(a, b, fx), MathHelper.Lerp(c, d, fx), fy);
    }

    public float Fbm(float x, float y, int octaves)
    {
        float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
        for (var i = 0; i < octaves; i++)
        {
            sum += Value(x * freq, y * freq) * amp;
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }
        return sum / norm;
    }
}

// Minimal RGB PNG writer (8-bit, color type 2), zlib via ZLibStream.
static class Png
{
    public static void Write(string path, int width, int height, byte[] rgb)
    {
        using var fs = File.Create(path);
        Span<byte> sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        fs.Write(sig);

        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)width);
        WriteBE(ihdr, 4, (uint)height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 2;   // color type: truecolor RGB
        Chunk(fs, "IHDR", ihdr);

        // Filter type 0 per scanline.
        var raw = new byte[height * (width * 3 + 1)];
        var stride = width * 3;
        for (var y = 0; y < height; y++)
        {
            raw[y * (stride + 1)] = 0;
            Array.Copy(rgb, y * stride, raw, y * (stride + 1) + 1, stride);
        }

        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true))
        {
            z.Write(raw, 0, raw.Length);
        }
        Chunk(fs, "IDAT", ms.ToArray());
        Chunk(fs, "IEND", []);
    }

    static void WriteBE(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, (uint)data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        var crc = Crc32(t, data);
        var c = new byte[4];
        WriteBE(c, 0, crc);
        s.Write(c);
    }

    static readonly uint[] CrcTable = BuildCrc();
    static uint[] BuildCrc()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    static uint Crc32(byte[] a, byte[] b)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var x in a) crc = CrcTable[(crc ^ x) & 0xFF] ^ (crc >> 8);
        foreach (var x in b) crc = CrcTable[(crc ^ x) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
