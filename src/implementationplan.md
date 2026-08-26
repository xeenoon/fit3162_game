# Implementation Plan — Physics-First Terrain via Stream-Power Erosion

**Goal:** make the procedural world map as realistic as (or more realistic than)
*StreamPowerErosion* by Schott, Paris, Fournier, Guérin & Galin (ACM TOG 2023),
while **keeping everything we already author**. See `ATTRIBUTIONS.md` for citation.

**Core idea:** stop generating terrain shape from noise + a ridge graph and then
*analysing* drainage after the fact. Instead, let ridges, valleys and drainage
**emerge from an erosion simulation**, driven by an uplift field we *derive from
our existing ridge graph + hill layout*. This preserves authorial control over
where mountains/valleys/lakes go, but makes the landform hydrologically consistent
— which is the thing noise fundamentally cannot fake.

Everything is CPU-side, deterministic per seed, and runs at worldgen time. No GPU
compute is required (the paper's GPU work exists only for interactive painting,
which we don't need).

---

## Background: what we're copying and why it works

Landforms are *emergent structures resulting from the competition between uplift and
erosion* (Schott et al. 2023). The governing law (their Eq. 1), from geomorphology
(Whipple & Tucker 1999; brought to graphics by Cordonnier et al. 2016):

```
∂h/∂t = u − k · sⁿ · aᵐ + k_h·Δh − k_d·s
```

- `u`   — uplift (tectonic rise rate) — **we derive this from our systems**
- `s`   — local slope to the downstream receiver
- `a`   — drainage area (how much water flows through the cell)
- `sⁿaᵐ`— stream-power fluvial incision; exponent ratio **m/n ≈ 0.4–0.5** is a physical law
- `k_h·Δh` — hillslope diffusion (soil creep; rounds ridge crests)
- `k_d·s`  — debris-flow term (caps slopes near the angle of repose → talus/scree)

Integrated explicitly (their Eq. 2) with the invariant `h = max(h, h_receiver)` so a
cell never incises below its outlet (no spurious pits), and borders pinned to a base
level so water exits the domain. Run to equilibrium → dendritic drainage networks with
correct concave valley profiles.

**Two places we already beat the paper's shipped code:**
1. They approximate drainage area (Eq. 5) to run *in parallel on GPU*. Offline we can
   compute it **exactly** via elevation-sorted accumulation — simpler and more accurate.
2. Their shipped shader routes all water to the single steepest neighbour (D8). The
   paper prefers an **Lᵖ multi-neighbour split with p ≈ 4** (Eq. 4), which produces
   natural branching instead of harsh single-thread channels. We implement Lᵖ.

---

## What we leverage from the existing codebase

| Existing piece | Location | Reused for |
| --- | --- | --- |
| Grid heightfield + `GridResolution` scaling (193/241) | `HillsWorld.cs` `GeneratedTerrain` | erosion operates in place on this grid |
| Ridge graph (`RidgeContribution`, `GenerateRidgelineGraph`) | `HillsWorld.cs:720` | source of the **uplift field** |
| Macro hills (`GenerateMacroHills`) | `HillsWorld.cs:719` | lowland uplift |
| `FillPits` (priority-flood) | `HillsWorld.cs:1038` | initial pit handling before erosion |
| `CalculateDrainageFlow` (D8 accumulation) | `HillsWorld.cs:821` | starting point for the exact area pass |
| `RecalculateTerrain` (normals/surfaces/bands) | `HillsWorld.cs:2554` | re-derive after erosion changes height |
| `TerrainMaterialTexture.Compose` | `TerrainMaterialTexture.cs:35` | swap noise masks → eroded-field masks |
| Poly Haven PBR library | `textures/` + `textures/README.md` | materials driven by slope/drainage/curvature |
| Orthophoto bake tool | `tools/orthophoto/` | before/after hillshade validation |

**What we replace (the fake bits):** `TraceDrainageLines` + `CarveDrainageErosion`
(`HillsWorld.cs:822–823`) and `DepositScreeFans` (`:826`) — swapped for emergent
drainage and talus.

---

## Phase 0 — Prove it standalone (before touching the game)

Build the eroder in `tools/erode/` first (mirroring `tools/orthophoto/`), so we tune
and eyeball before integrating.

- Generate a heightfield → run N erosion iterations → output hillshade **before/after**
  plus a **drainage-network overlay** (channels where `a` exceeds a threshold), as PNGs.

**Acceptance criteria:**
- Dendritic valley networks visibly emerge.
- **Slope–area log-log plot is roughly linear** (the objective geomorphology test — if
  it isn't, the solver is wrong).
- Talus slopes cap near a constant angle (debris term working).

Do not proceed to integration until these hold.

---

## Phase 1 — Core eroder (`StreamPowerErosion`, credited to Schott et al. 2023)

A single class operating on `float[] heights`.

- **State per cell:** `h`, `a`, derived `receiver`/`slope`.
- **Drainage area:** exact, elevation-sorted high→low accumulation (better than Eq. 5).
- **Flow routing:** Lᵖ multi-neighbour split, `p ≈ 4` (Eq. 4).
- **Update:** explicit Euler of the governing law above, with `h = max(h, h_receiver)`.
- **Boundary:** borders fixed to base level 0 (outlets).
- **Terms:** include hillslope diffusion (`k_h·Δh`) and debris flow (`k_d·s`).
- **Params:** `m ≈ 0.8`, `n ≈ 2` (keep m/n ≈ 0.4–0.5); tune `k`, `k_h`, `k_d`, `dt`,
  iteration count (≈150–300 at 193², more at higher res).
- **Stability:** the receiver-clamp + modest `dt` keep explicit integration stable.

If any file copies their GLSL/C++ verbatim it carries the MIT notice; our port is a
from-scratch C# re-implementation of the *method*, credited to Schott et al.

---

## Phase 2 — Derive uplift from existing systems (the "leverage" core)

Build the uplift field `u0` from what `GenerateTerrain` already computes — no painting:

- **High** uplift along the ridge graph / where `mountainInfluence` is high → mountains.
- **Moderate** uplift under macro hills → rolling lowland relief.
- **Low/zero** uplift in basins and toward edges (reuse existing `edgeFalloff`).
- **Base level / outlets:** map borders + existing lake sites as fixed low points so the
  network grades to them.

Result: the layout you designed is preserved; erosion makes it real. (Later fallback:
the paper's inverse procedural modelling — reconstruct uplift from the current
heightfield — if deriving from the graph proves fiddly.)

---

## Phase 3 — Re-integrate downstream systems

Erosion changes height, so height-derived systems run after it:

- `RecalculateTerrain` — recompute normals/surfaces/mountain bands from eroded height.
- Site selection, path carving, prop scatter, `CalculateAmbientAccessibility` already run
  after terrain in `Generate` (`HillsWorld.cs:595–684`); ensure they read post-erosion height.
- **Tests:** `Tests/HillsWorldGenerationTests.cs` asserts terrain properties
  (`RavineCutCount`, `MaxAdjacentInfluenceDelta`, fingerprints). Erosion changes geometry
  substantially → these need deliberate **rebaselining** (expected, not a regression).

---

## Phase 4 — Materials driven by geomorphology (appearance win)

Replace noise masks in `TerrainMaterialTexture.cs` with eroded fields:

| Eroded field | Drives |
| --- | --- |
| Slope `s` | bare rock / scree on steep faces; soil + grass on gentle ground |
| Drainage area `a` (log) | wet dark rock, gravel riverbeds, sediment, real stream channels |
| Curvature (Laplacian) | dry bright convex ridges vs moist dark concave valleys; snow in hollows |
| Deposition (Phase 5) | alluvial fans, flat valley-floor sediment, lake deltas |

Combine with the earlier renderer fixes — **use the normal + roughness maps we already
downloaded**, triplanar rock, and a **dense canopy layer instead of flat cones** — to
reach their surface quality or better.

---

## Phase 5 — Exceed them (deliberate additions)

Their method is **detachment-limited only** (erodes, never deposits; they cite Yuan et al.
2019 as out of scope). Ways to be *more* realistic:

1. **Sediment deposition / transport** — settle eroded material into **alluvial fans,
   flat-floored valleys, lake deltas**. Biggest realism feature they lack.
2. **Resolution + detail transfer** — erode at 512²/1024² offline; keep a gameplay-res
   heightfield but bake high-res eroded detail into **displacement/normal maps** for
   rendering. Fine detail, no runtime cost.
3. **Variable bedrock hardness / strata** (Cordonnier et al. 2018) — modulate `k` by a
   strata field → folded, layered cliff faces instead of uniform rock.
4. Keep hillslope + debris terms on for consistent soil creep and talus everywhere.

---

## Phase 6 — Validation loop (every phase)

- Hillshade before/after via `tools/orthophoto`.
- Drainage-network overlay.
- **Slope–area log-log linearity** (objective correctness test).
- Talus-angle histogram (should cluster near the debris threshold).

---

## Milestones

1. **P0 / P1** — standalone eroder + proof images → dendritic valleys, linear slope–area.
2. **P2 / P3** — uplift from ridge graph, wired into `GenerateTerrain`, tests rebaselined
   → game generates eroded worlds deterministically.
3. **P4** — materials driven by eroded fields → textures follow landform.
4. **P5** — deposition + resolution + strata → past their realism.

**Risks:** solver stability (mitigated by receiver-clamp + tuned `dt`); test rebaselining
(expected). Determinism preserved throughout (seeded, fixed iteration count).

---

## References

- Schott, Paris, Fournier, Guérin, Galin. *Large-Scale Terrain Authoring through
  Interactive Erosion Simulation.* ACM TOG 42(5), 2023. DOI 10.1145/3592787.
  Repo: <https://github.com/H-Schott/StreamPowerErosion> (MIT © 2023 Hugo Schott).
- Cordonnier et al., 2016 — Stream Power erosion in graphics.
- Whipple & Tucker, 1999 — Stream Power incision law (geomorphology).
- Yuan et al., 2019 — sediment deposition (basis for Phase 5).
- Cordonnier et al., 2018 — bedrock strata / folds (basis for Phase 5.3).
