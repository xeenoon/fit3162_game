using Game;
using System;
using System.Linq;

public sealed class StreamPowerErosionTests
{
    [Fact]
    public void Run_IsDeterministicAndPinsBoundary()
    {
        const int size = 25;
        var source = Enumerable.Range(0, size * size).Select(i => 1f + (i % size) * 0.01f).ToArray();
        var uplift = Enumerable.Repeat(0.003f, source.Length).ToArray();
        var eroder = new StreamPowerErosion(size, size, 1f, new(Iterations: 12));

        var first = eroder.Run((float[])source.Clone(), uplift).Heights;
        var second = eroder.Run((float[])source.Clone(), uplift).Heights;

        Assert.Equal(first, second);
        Assert.All(Enumerable.Range(0, size), x => Assert.Equal(0f, first[x]));
        Assert.All(first, value => Assert.True(float.IsFinite(value)));
    }

    [Fact]
    public void Analyse_SplitsFlowAcrossMultipleDownhillNeighbours()
    {
        var heights = new float[]
        {
            0, 0, 0,
            0, 2, 0,
            0, 0, 0,
        };
        var result = new StreamPowerErosion(3, 3, 1f).Analyse(heights);
        var cardinal = result.DrainageArea[1];
        var diagonal = result.DrainageArea[0];

        Assert.True(cardinal > 1f);
        Assert.True(diagonal > 1f);
        Assert.True(cardinal > diagonal);
    }

    [Fact]
    public void HardBedrockResistsIncision()
    {
        const int size = 17;
        var source = Enumerable.Range(0, size * size)
            .Select(i => 8f - MathF.Abs(i % size - size / 2) * 0.08f).ToArray();
        var uplift = new float[source.Length];
        var soft = Enumerable.Repeat(0.5f, source.Length).ToArray();
        var hard = Enumerable.Repeat(2f, source.Length).ToArray();
        var eroder = new StreamPowerErosion(size, size, 1f, new(Iterations: 24));

        var softResult = eroder.Run((float[])source.Clone(), uplift, soft).Heights;
        var hardResult = eroder.Run((float[])source.Clone(), uplift, hard).Heights;

        Assert.True(hardResult.Average() > softResult.Average());
    }
}
