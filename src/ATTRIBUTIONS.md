# Attributions

Third-party algorithms, code, and assets used in this project, with sources so
they can be traced back. Textures shipped with the game live under `textures/`
and are documented separately in [`textures/README.md`](textures/README.md)
(all Poly Haven, CC0).

---

## Stream Power erosion (terrain geometry)

**Source repository:** StreamPowerErosion — <https://github.com/H-Schott/StreamPowerErosion>
**License:** MIT © 2023 Hugo Schott (see `References/StreamPowerErosion/LICENSE`).
The MIT copyright notice must be reproduced in any file that ports a substantial
portion of their code.

**Paper:** Hugo Schott, Axel Paris, Lucie Fournier, Éric Guérin, Éric Galin.
*Large-Scale Terrain Authoring through Interactive Erosion Simulation.*
ACM Transactions on Graphics 42(5), 2023. DOI: 10.1145/3592787.
Preprint: <https://hal.science/hal-04049125v1> (`2022-uplift-author.pdf`).

**Algorithm lineage** (cite whichever we lean on):
- Stream Power incision law, geomorphology origin — **Whipple & Tucker, 1999**.
- Introduction of the Stream Power law to computer graphics terrain — **Cordonnier et al., 2016**.
- The specific parallel drainage-area approximation + uplift-driven authoring we
  are porting — **Schott, Paris, Fournier, Guérin, Galin, 2023** (above).
  The GPU compute kernel `data/shaders/spe_shader.glsl` is by **Hugo Schott**.

**What we take:** the erosion update rule
`∂h/∂t = u − sⁿ·aᵐ + Δh` (Eq. 1) integrated explicitly (Eq. 2), i.e. per-cell
drainage-area accumulation + stream-power incision (+ optional hillslope
Laplacian diffusion and debris-flow slope terms). Our port is a from-scratch C#
re-implementation of the *method*, credited to Schott et al.; if any file copies
their GLSL/C++ verbatim it will carry the MIT notice above.

---

## Textures / data taken from StreamPowerErosion

Nothing yet. If we use any raster from `References/StreamPowerErosion/data/`
(e.g. the uplift maps `alpes_noise.png`, `lambda.png`, or heightfields
`hfTest1.png`, `hfTest2.png`) as a test input, list it here with its path and
role. All are covered by the repo's MIT license © 2023 Hugo Schott.

| File taken | Used as | Notes |
| --- | --- | --- |
| _(none yet)_ | | |

---

## EarthGen (reference only)

*EarthGen: Generating the World from Top-Down Views* — Sharma et al.,
arXiv:2409.01491. Studied as a reference for multi-scale satellite appearance
(`References/EarthGen/`). No code or weights are shipped in the game.
