# Stream-power erosion proof tool

This CPU-only tool generates a deterministic synthetic heightfield, applies the same
`StreamPowerErosion` class intended for game integration, and writes:

- `before.png` and `after.png` hillshades;
- `drainage.png`, with exact Lp multiple-flow accumulation overlaid in blue;
- `slope-area.csv`, raw samples suitable for plotting on log-log axes;
- `metrics.txt`, including a regression over logarithmic drainage-area bins and a
  talus-angle occupancy measurement.

Run from the repository root:

```sh
dotnet run --project tools/erode/Erode.csproj -- --out tools/erode/proof
```

The model is a from-scratch C# implementation of the method described by Schott,
Paris, Fournier, Guérin and Galin (2023). The vendored reference shader at
`References/StreamPowerErosion/data/shaders/spe_shader.glsl` was used to cross-check
the stream-power, diffusion, debris-flow and fixed-boundary terms; this implementation
uses exact elevation-sorted drainage and Lp multi-neighbour routing instead of the
reference shader's parallel D8 approximation.
