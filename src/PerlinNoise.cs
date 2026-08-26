using System;

namespace Game;

/// <summary>A small, seeded implementation of improved two-dimensional Perlin noise.</summary>
public sealed class PerlinNoise
{
    private readonly int[] _permutation = new int[512];

    public PerlinNoise(int seed)
    {
        var values = new int[256];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = i;
        }

        var random = new Random(seed);
        for (var i = values.Length - 1; i > 0; i--)
        {
            var swapIndex = random.Next(i + 1);
            (values[i], values[swapIndex]) = (values[swapIndex], values[i]);
        }

        for (var i = 0; i < _permutation.Length; i++)
        {
            _permutation[i] = values[i & 255];
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

        var top = Lerp(
            Gradient(topLeft, localX, localY),
            Gradient(topRight, localX - 1f, localY),
            fadeX);
        var bottom = Lerp(
            Gradient(bottomLeft, localX, localY - 1f),
            Gradient(bottomRight, localX - 1f, localY - 1f),
            fadeX);

        // Improved Perlin noise is approximately in [-1, 1].
        return Lerp(top, bottom, fadeY) * 0.5f + 0.5f;
    }

    public float Fractal(float x, float y, int octaves = 4, float persistence = 0.52f)
    {
        var total = 0f;
        var amplitude = 1f;
        var amplitudeTotal = 0f;
        var frequency = 1f;

        for (var octave = 0; octave < octaves; octave++)
        {
            total += Sample(x * frequency, y * frequency) * amplitude;
            amplitudeTotal += amplitude;
            amplitude *= persistence;
            frequency *= 2f;
        }

        return total / amplitudeTotal;
    }

    private static float Fade(float value) =>
        value * value * value * (value * (value * 6f - 15f) + 10f);

    private static float Lerp(float from, float to, float amount) => from + amount * (to - from);

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
