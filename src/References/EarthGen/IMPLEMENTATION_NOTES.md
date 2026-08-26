# EarthGen implementation notes

Reference paper: **EarthGen: Generating the World from Top-Down Views** (Sharma et al., arXiv:2409.01491v2, 2025 revision).

Local PDF: `EarthGen-2409.01491v2.pdf`.

Related material:

- Paper HTML: <https://arxiv.org/html/2409.01491v2>
- Project/demo: <https://earthgen.github.io/>
- Released research code: <https://github.com/anshgs/earthgen>
- Paper model checkpoints: linked from that repository’s README.

## Read this before copying the method

EarthGen is **not a procedural 3D terrain generator**. It does not generate a heightfield from ridges, erosion, or physical simulation. It generates a *2D orthographic RGB image that resembles satellite imagery*, then demonstrates a separate, approximate RGB-to-depth conversion to obtain a mesh.

That distinction matters:

- It is an excellent reference for the satellite-image appearance you want: multi-scale land-cover detail, coherent large features, and seamless large-area generation.
- It is not sufficient for reliable game geometry, collision, paths, or prop placement. Its depth is inferred from the generated image and can invent or distort physical terrain.
- A game-quality solution should keep a real heightfield as the authoritative world and use EarthGen-like multi-scale synthesis for its terrain appearance/masks.

## What EarthGen actually does

```text
Satellite-image pyramid dataset
        ↓
Fine-tune a satellite-specific VAE shared by all scales
        ↓
Train an unconditional low-zoom latent diffusion base model
        ↓
Train one independent x4 latent-diffusion upscaler per zoom transition
        ↓
Generate a coarse regional image
        ↓
Run repeated x4 super-resolution passes
        ↓
At every pass, jointly blend overlapping tile denoising predictions
        ↓
Decode overlapping latent tiles and blend the RGB results
        ↓
Optional: guide base generation using a ControlNet map/layout
        ↓
Optional: infer depth from generated RGB and triangulate it into a mesh
```

It has up to five hierarchy levels and a total $1024×$ resolution increase. The paper’s full demonstration covers 30 km x 10 km at 15 cm/pixel.

## Exact paper pipeline

### 1. Train from a nested, multi-resolution image pyramid

They query Bing Maps for *concentric* satellite-image stacks. Every stack describes the exact same geographical area at every zoom level, so each low-resolution tile has a correctly aligned high-resolution target.

- Each sampled image is 2048 x 2048 pixels.
- Zoom levels range from 10 to 20.
- Zoom 20 is approximately 15 cm/pixel; each lower zoom halves linear resolution.
- Samples are taken between latitudes -66 and 66 to limit map-projection distortion.
- Dataset: 32,000 stacks available to zoom 19, plus 25,000 to zoom 20.
- They also collect 12,000 urban-biased stacks for city coverage.

**Copyable principle:** every detail level must be spatially aligned with the preceding level. Do not independently add “random detail” without preserving the macro layout it belongs to.

### 2. Fine-tune the VAE for satellite imagery

They first adapt Stable Diffusion’s VAE across *all* dataset resolutions, because its stock VAE did not represent low-zoom satellite imagery well.

- Optimise reconstruction with MSE + KL regularisation + LPIPS perceptual loss.
- Reported weights: KL = `1e-9`, LPIPS = `0.1`.
- Freeze the VAE after this stage.

**Why it matters:** it gives the later diffusion modules an image representation that preserves terrain/land-cover statistics at both regional and close scales.

### 3. Generate global structure at low resolution

The base layer is an unconditional latent diffusion model. It samples a coarse, broad land-cover layout before fine detail exists.

At this scale, it establishes:

- mountain/ridge and valley regions;
- forest versus open ground;
- rivers/lakes and broad drainage;
- regional colour/climate; and
- roads/cities in the general model.

**Copyable principle:** make high-level terrain and biome decisions once, at low frequency, and preserve them. Fine layers decorate a defined valley/forest/ridge; they must never replace it with unrelated local content.

### 4. Use a separate x4 generative super-resolution model per scale

They chain independent Stable Diffusion x4 Upscaler-derived latent diffusion modules: `10→12`, `12→14`, through `18→20`.

- Each module receives its matching low-resolution view as a condition.
- It predicts plausible new detail rather than merely interpolating pixels.
- Each module is trained independently on 128-pixel low-resolution / 512-pixel high-resolution paired crops.
- Each stage is trained for 100,000 steps, batch size 24.
- The paper reports Adam at learning rate `1e-6`.

**What each level conceptually adds for your terrain:**

| Level | Satellite equivalent | Game-world equivalent |
| --- | --- | --- |
| Regional | range position, climate, broad land cover | biome, ridge graph, valley network, large forest masses |
| Landscape | watershed, large clearings, lakes, main roads | erosion basins, major forest clearings, main paths, dungeon regions |
| Landform | gullies, scree fans, forest edge shape | terrain deformation, cliff/scree masks, canopy boundaries |
| Ground cover | rocks, shrubs, tracks, soil transitions | instanced rocks/shrubs, material blends, path edge debris |
| Fine material | individual vegetation/rock/soil texture | normal/roughness/detail texture and small decals |

### 5. Use negative conditioning to stop error accumulation

Repeated generated upscaling tends to compound defects. At inference, they steer every super-resolution stage away from a fixed negative text description: `blurry, low res, low quality`.

Their negative-guidance strengths for `10→12` through `18→20` are `5, 2, 3, 3, 4` respectively.

**Copyable principle without training AI:** each procedural detail pass needs explicit rejection rules. Examples: reject a tree that floats, reject a rock in water, reject grass on cliffs, reject a prop that collides with a path/entrance, reject high-frequency variation that destroys a low-frequency landform.

### 6. Do not stitch independently generated tiles

This is the part most directly relevant to your visible texture seams.

For every output pixel at a super-resolution level, EarthGen gets predictions from every overlapping input tile that covers it. Instead of generating tiles and stitching/blurring after the fact, it blends the **diffusion noise predictions at every denoising step** using Gaussian weights centred in each tile.

Paper settings:

- latent tile size: 128;
- stride: 64; therefore 50% overlap;
- blend: normalised Gaussian weight by distance from each tile’s centre;
- VAE decode: overlapping latent windows of 512 with overlap coefficient 0.25, then linear RGB blending.

Their ablation finds naive stitching creates seams; post-generation Gaussian compositing hides seams but blurs; their per-denoising-step mixture is the cleanest.

**Game equivalent:**

- Generate masks/material variation from a single global coordinate system, never a per-chunk random origin.
- Create an overlap border around every terrain chunk.
- Evaluate erosion, flow, and macro masks beyond chunk edges.
- Blend chunk-border outputs using a smooth/normalised spatial weight.
- Do not separately randomise tree density/texture offsets at chunk boundaries.
- For terrain textures, use a global macro colour/mask atlas; detailed tileable materials sit beneath it.

### 7. Optional controllable generation

They add a ControlNet to the base layer so a low-resolution map/layout can control the generated result while the system fills in visual detail. This is only a demonstration, not the main model.

**Best analogue for the game:** make a low-resolution authoring/control map with channels such as:

```text
height / ridge
erosion or flow
forest density
wetness
snow
rock exposure
path / road
dungeon exclusion and landmark zones
```

Every later geometry, material, and vegetation pass should be conditioned on those masks. This is the non-ML version of their controllable-generation result.

### 8. Their 3D step is only image-to-depth reconstruction

For the paper’s 3D demo they use the off-the-shelf DepthAnything estimator, turn RGB-D into a point cloud, and triangulate it. They report that roads and approximate elevations are visible.

**Do not make this your main game-terrain approach.** It cannot guarantee correct playable slopes, collision, water flow, or consistent entrances. Use it only as a concept-art/previsualisation experiment, or use its generated orthophoto as a macro colour reference projected onto a real heightfield.

## Accurate implementation plan for this game

The closest reliable version of EarthGen for the existing MonoGame project is a **deterministic, multi-scale control-map pipeline**, not retraining their diffusion system.

### Authoritative world data

Keep this data global and seeded:

```text
World seed
Biome regions
Ridge graph and broad heightfield
Hydrology: filled heightfield, downhill flow, catchments, rivers
Erosion/debris/sediment fields
Dungeon sites and path splines
```

### Derived control maps, all in world coordinates

Build 256–1024-resolution maps for:

```text
Elevation        slope             curvature
Flow             wetness           distance to water
Forest density   tree-line         clearing mask
Rock exposure    scree/talus       snow accumulation
Sun aspect       ambient occlusion distance to path
Macro colour     dungeon exclusion landmark mask
```

### Produce detail at five controlled scales

| Pass | Typical footprint | Output | EarthGen analogue |
| --- | --- | --- | --- |
| 1: regional | whole map | biome, climate, range/valley layout | base diffusion image |
| 2: landscape | 20–50% of map | ridges, watersheds, main forest regions | first super-resolution |
| 3: landform | 5–20% of map | gullies, cliffs, talus fans, streams, clearings | mid-scale super-resolution |
| 4: ecosystem | 1–5% of map | canopy patches, tree species/density, boulder fields, shrubs | high-resolution detail |
| 5: surface | under 1% of map | PBR material blend, normal/roughness, pebbles/decals | finest imagery detail |

At no pass should output overturn a decision above it. A material pass may make a forested slope darker and patchier; it cannot turn it into bare sand. A shrub pass can add undergrowth; it cannot cover a cliff.

### Generate satellite-like appearance, not random texture

For every terrain pixel, compute a final *macro albedo* from world maps:

```text
base biome palette
  + large colour variation (hundreds of metres)
  + slope/aspect brightness and dryness
  + flow/wetness darkening
  + land-cover colour (forest / grass / rock / snow)
  + erosion debris/sediment colour
```

Then blend close material textures based on the same masks. The macro albedo should remain visible at distance; close textures should supply grain, normal, and roughness at short range.

### Forest must have two representations

```text
far/middle distance: continuous canopy colour + canopy normal/height/noise field
near distance:       instanced tree meshes, shrubs, trunks, contact shadows
```

This is essential. Individual low-poly trees across a whole map can never resemble satellite imagery because forests in satellite imagery are dense texture fields, not sparse objects.

### Seam-free chunk contract

For any chunk, evaluate all fields from absolute world position and include a border wider than your largest operation.

```text
terrain chunk interior
  + 1–2 erosion-kernel radius of generated border
  + global masks sampled with world UVs
  + prop candidates generated from a global spatial hash/cell ID
  + discard/crop the border only after generation
```

Never seed `Random` directly by a local chunk index for visible fields. Derive a stable global cell seed from world seed + integer world-cell coordinates instead.

## If you literally want to use EarthGen

This is a separate, research/GPU-heavy content pipeline:

1. Obtain the authors’ released checkpoints from their Hugging Face link, or collect imagery permitted for model training.
2. Run their Python/PyTorch system offline on a CUDA-capable machine.
3. Generate a large 2D orthographic terrain image, preferably conditioned by a low-resolution mask/layout matching your designed world.
4. Export it as a **macro colour atlas**, not as the sole terrain texture.
5. Keep your deterministic heightfield for collision and use generated image segmentation/material masks to guide forest, grass, rock, paths, and decals.
6. Bake the result into game-ready 2K/4K texture atlases and stream/clipmap them by distance.

Do not invoke this at game runtime. The authors use trained latent diffusion modules, 50 sampling steps, multiple x4 stages, and overlapping-tile inference. It is content baking, not a real-time MonoGame feature.

## Priority order for the current implementation

1. Global coordinate-based terrain masks and chunk overlap: fixes seams and inconsistent detail.
2. Dense far-field canopy layer: removes the sparse-object/unfinished-map appearance.
3. Erosion/flow/debris masks: gives mountains gullies, scree, wet valleys, and material logic.
4. Macro albedo map at world scale: eliminates the repeated, uniform terrain colour.
5. Real close-range materials with triplanar rock: adds surface realism.
6. Two-level vegetation and rock rendering: detail only where a camera can resolve it.
7. Optionally bake an EarthGen-like generated orthophoto as a macro overlay.

## References and licensing note

The paper is an arXiv preprint. Its released code repository does not visibly state a software licence in its root listing at the time of this review; inspect the repository before copying code. Bing Maps imagery and model-checkpoint terms are separate from the paper and must be reviewed before commercial redistribution or training use.
