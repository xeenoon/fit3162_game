using System.IO.Compression;
using Game;

var size = 193;
var iterations = 240;
var seed = 2026;
var output = Path.GetFullPath("erode-proof");
for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--size": size = int.Parse(args[++i]); break;
        case "--iterations": iterations = int.Parse(args[++i]); break;
        case "--seed": seed = int.Parse(args[++i]); break;
        case "--out": output = Path.GetFullPath(args[++i]); break;
    }
}

Directory.CreateDirectory(output);
var initial = BuildInitialTerrain(size, seed);
var uplift = BuildUplift(size, seed);
var eroder = new StreamPowerErosion(size, size, 1f, new(
    Iterations: iterations,
    TimeStep: 0.08f,
    Erodibility: 0.00055f,
    HillslopeDiffusion: 0.045f,
    DebrisFlow: 0.35f));
var before = eroder.Analyse((float[])initial.Clone());
var result = eroder.Run((float[])initial.Clone(), uplift);
var regression = SlopeAreaRegression(result.DrainageArea, result.Slopes);
var talusFraction = result.Slopes.Count(s => s is >= 0.50f and <= 0.66f) / (float)result.Slopes.Count(s => s > 0.05f);

Png.Write(Path.Combine(output, "before.png"), size, size, Hillshade(before.Heights, size));
Png.Write(Path.Combine(output, "after.png"), size, size, Hillshade(result.Heights, size));
Png.Write(Path.Combine(output, "drainage.png"), size, size, DrainageOverlay(result.Heights, result.DrainageArea, size));
File.WriteAllText(Path.Combine(output, "slope-area.csv"), BuildSlopeAreaCsv(result.DrainageArea, result.Slopes));
File.WriteAllText(Path.Combine(output, "metrics.txt"),
    $"seed={seed}\nsize={size}\niterations={iterations}\nslope_area_samples={regression.Count}\nslope_area_slope={regression.Slope:F4}\nslope_area_r_squared={regression.RSquared:F4}\ntalus_fraction_0.50_to_0.66={talusFraction:F4}\nmax_drainage_area={result.DrainageArea.Max():F2}\n");

Console.WriteLine($"Stream-power erosion proof: {size}x{size}, {iterations} iterations, seed {seed}");
Console.WriteLine($"Slope-area log-log: n={regression.Count}, slope={regression.Slope:F4}, R²={regression.RSquared:F4}");
Console.WriteLine($"Talus band [0.50, 0.66]: {talusFraction:P1} of non-flat cells");
Console.WriteLine($"Maximum drainage area: {result.DrainageArea.Max():F2} cells");
Console.WriteLine($"Wrote {output}/before.png, after.png, drainage.png, slope-area.csv, metrics.txt");

static float[] BuildInitialTerrain(int size, int seed)
{
    var random = new Random(seed);
    var impulses = Enumerable.Range(0, 28)
        .Select(_ => (X: random.NextSingle(), Y: random.NextSingle(), Radius: 0.06f + random.NextSingle() * 0.16f, Height: 0.18f + random.NextSingle() * 0.42f))
        .ToArray();
    var result = new float[size * size];
    for (var y = 0; y < size; y++)
    for (var x = 0; x < size; x++)
    {
        var u = x / (float)(size - 1);
        var v = y / (float)(size - 1);
        var edge = MathF.Min(MathF.Min(u, 1f - u), MathF.Min(v, 1f - v));
        var h = MathF.Min(1f, edge * 10f) * 0.12f;
        foreach (var hill in impulses)
        {
            var dx = u - hill.X;
            var dy = v - hill.Y;
            h += hill.Height * MathF.Exp(-(dx * dx + dy * dy) / (2f * hill.Radius * hill.Radius));
        }
        result[y * size + x] = h * 28f;
    }
    return result;
}

static float[] BuildUplift(int size, int seed)
{
    var uplift = new float[size * size];
    for (var y = 1; y < size - 1; y++)
    for (var x = 1; x < size - 1; x++)
    {
        var u = x / (float)(size - 1);
        var v = y / (float)(size - 1);
        var edge = MathF.Min(MathF.Min(u, 1f - u), MathF.Min(v, 1f - v));
        var ridge = MathF.Exp(-MathF.Pow((u - 0.5f) + 0.15f * MathF.Sin(v * 12f + seed), 2f) / 0.018f);
        uplift[y * size + x] = (0.008f + 0.028f * ridge) * MathF.Min(1f, edge * 8f);
    }
    return uplift;
}

static (int Count, double Slope, double RSquared) SlopeAreaRegression(float[] area, float[] slope)
{
    // Regress logarithmic bin means: individual cells naturally have large local scatter,
    // while the geomorphology test concerns the trend across drainage-area scales.
    var points = area.Zip(slope).Where(p => p.First >= 8f && p.Second > 1e-5f)
        .Select(p => (X: Math.Log(p.First), Y: Math.Log(p.Second)))
        .GroupBy(p => (int)Math.Floor(p.X / 0.5))
        .Where(bin => bin.Count() >= 8)
        .Select(bin => (X: bin.Average(p => p.X), Y: bin.Average(p => p.Y)))
        .ToArray();
    var mx = points.Average(p => p.X);
    var my = points.Average(p => p.Y);
    var sxx = points.Sum(p => (p.X - mx) * (p.X - mx));
    var sxy = points.Sum(p => (p.X - mx) * (p.Y - my));
    var slopeFit = sxy / sxx;
    var intercept = my - slopeFit * mx;
    var residual = points.Sum(p => Math.Pow(p.Y - (intercept + slopeFit * p.X), 2));
    var total = points.Sum(p => Math.Pow(p.Y - my, 2));
    return (points.Length, slopeFit, 1d - residual / total);
}

static string BuildSlopeAreaCsv(float[] area, float[] slope)
{
    var lines = new List<string> { "drainage_area,slope,log_area,log_slope" };
    for (var i = 0; i < area.Length; i++)
        if (area[i] >= 8f && slope[i] > 1e-5f)
            lines.Add($"{area[i]:F6},{slope[i]:F6},{Math.Log(area[i]):F6},{Math.Log(slope[i]):F6}");
    return string.Join('\n', lines) + "\n";
}

static byte[] Hillshade(float[] h, int size)
{
    var rgb = new byte[size * size * 3];
    for (var y = 0; y < size; y++)
    for (var x = 0; x < size; x++)
    {
        var l = h[y * size + Math.Max(0, x - 1)];
        var r = h[y * size + Math.Min(size - 1, x + 1)];
        var d = h[Math.Max(0, y - 1) * size + x];
        var u = h[Math.Min(size - 1, y + 1) * size + x];
        var nx = (l - r) * 4f;
        var ny = 2f;
        var nz = (d - u) * 4f;
        var inv = 1f / MathF.Sqrt(nx * nx + ny * ny + nz * nz);
        var light = Math.Clamp((nx * -0.45f + ny * 0.82f + nz * -0.35f) * inv, 0f, 1f);
        var value = (byte)(35 + light * 210);
        var i = (y * size + x) * 3;
        rgb[i] = rgb[i + 1] = rgb[i + 2] = value;
    }
    return rgb;
}

static byte[] DrainageOverlay(float[] heights, float[] area, int size)
{
    var rgb = Hillshade(heights, size);
    for (var i = 0; i < area.Length; i++)
    {
        var channel = Math.Clamp((MathF.Log2(MathF.Max(1f, area[i])) - 4f) / 7f, 0f, 1f);
        if (channel <= 0f) continue;
        rgb[i * 3] = (byte)(rgb[i * 3] * (1f - channel));
        rgb[i * 3 + 1] = (byte)(rgb[i * 3 + 1] * (1f - channel) + 95f * channel);
        rgb[i * 3 + 2] = (byte)(rgb[i * 3 + 2] * (1f - channel) + 240f * channel);
    }
    return rgb;
}

static class Png
{
    public static void Write(string path, int width, int height, byte[] rgb)
    {
        using var stream = File.Create(path);
        stream.Write(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a });
        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width); WriteBigEndian(header, 4, (uint)height);
        header[8] = 8; header[9] = 2;
        Chunk(stream, "IHDR", header);
        var raw = new byte[height * (width * 3 + 1)];
        for (var y = 0; y < height; y++) Array.Copy(rgb, y * width * 3, raw, y * (width * 3 + 1) + 1, width * 3);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true)) zlib.Write(raw);
        Chunk(stream, "IDAT", compressed.ToArray()); Chunk(stream, "IEND", []);
    }

    static void WriteBigEndian(byte[] bytes, int offset, uint value)
    { bytes[offset] = (byte)(value >> 24); bytes[offset + 1] = (byte)(value >> 16); bytes[offset + 2] = (byte)(value >> 8); bytes[offset + 3] = (byte)value; }
    static void Chunk(Stream stream, string name, byte[] data)
    {
        var length = new byte[4]; WriteBigEndian(length, 0, (uint)data.Length); stream.Write(length);
        var type = System.Text.Encoding.ASCII.GetBytes(name); stream.Write(type); stream.Write(data);
        var crcBytes = new byte[4]; WriteBigEndian(crcBytes, 0, Crc(type, data)); stream.Write(crcBytes);
    }
    static uint Crc(byte[] type, byte[] data)
    {
        var crc = 0xffffffffu;
        foreach (var value in type.Concat(data))
        { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1; }
        return crc ^ 0xffffffffu;
    }
}
