# Terrain material library

This is a deliberately small, **tileable PBR** material library for the procedural world map. Every material is a 1K JPG export from [Poly Haven](https://polyhaven.com/), which releases its assets as [CC0](https://polyhaven.com/license).

## Tileability validation

All materials below were validated against Poly Haven's published material standard: a material must be seamless on all axes and free of noticeable tiling/cloning artefacts. Their source dimensions are square (recorded below) and none carries Poly Haven's `non square` tag. This makes them suitable for repeat wrapping on both terrain axes. Source: [Poly Haven texture requirements](https://docs.polyhaven.com/en/technical-standards/textures).

This is a source/metadata validation, not a claim that repeated 1K maps will be invisible at any scale. Use the macro layer and varied UV scale/rotation to hide repetition.

## Folder and scale conventions

- `macro/` = **high-level detail**. One large repeat across a broad terrain region; its job is kilometre-scale colour/landform variation, not visible grains.
- `detail/` = **low-level detail**. Repeats at its recorded real-world size; its job is ground material visible at close and middle distance.
- `shared/` = a detail material allowed in either biome.

Each material folder includes:

- `*_diff_*` or `*_col_*`: base colour / albedo
- `*_nor_gl_*`: OpenGL normal map
- `*_rough_*`: roughness map

## Hills biome

| Layer | Material | Use | Repeat size | Source |
| --- | --- | --- | --- | --- |
| Macro | `hills/macro/aerial_grass_rock` | Olive-green grass/rock variation: the broad colour breakup for hills. | 15 x 15 m | [Aerial Grass Rock](https://polyhaven.com/a/aerial_grass_rock) |
| Detail | `hills/detail/sparse_grass` | Main grass layer for open, gentle slopes. | 2 x 2 m | [Sparse Grass](https://polyhaven.com/a/sparse_grass) |
| Detail | `hills/detail/forest_ground_04` | Darker brown soil and stone: forest pockets, under trees, worn clearings. | 3.15 x 3.15 m | [Forest Ground 04](https://polyhaven.com/a/forest_ground_04) |
| Detail | `hills/detail/forrest_ground_03` | Pine needles and organic debris: use beneath conifers, not in open meadows. | 2 x 2 m | [Forest Ground 03](https://polyhaven.com/a/forrest_ground_03) |

## Mountain biome

| Layer | Material | Use | Repeat size | Source |
| --- | --- | --- | --- | --- |
| Macro | `mountains/macro/rocky_terrain_02` | Large green-grey mountain/rock pattern; breaks up huge lower slopes. | 90 x 90 m | [Rocky Terrain 02](https://polyhaven.com/a/rocky_terrain_02) |
| Macro | `mountains/macro/snow_field_aerial` | Large, irregular snow-field pattern for high elevation and lee-side snow masks. | 80 x 80 m | [Snow Field Aerial](https://polyhaven.com/a/snow_field_aerial) |
| Detail | `mountains/detail/dark_rock_02` | Primary cool dark cliff rock; apply with triplanar projection to steep faces. | 2.001 x 2.001 m | [Dark Rock 02](https://polyhaven.com/a/dark_rock_02) |
| Detail | `mountains/detail/rocks_ground_09` | Loose brown-grey scree/gravel below cliff faces and in run-off gullies. | 3 x 3 m | [Rocks Ground 09](https://polyhaven.com/a/rocks_ground_09) |
| Detail | `mountains/detail/lichen_rock` | Green-grey lichen rock for damp/shaded lower slopes; use sparingly to avoid a flat green mountain. | 2 x 2 m | [Lichen Rock](https://polyhaven.com/a/lichen_rock) |
| Detail | `mountains/detail/snow_02` | Close-range powdery snow texture for patches picked by the snow mask. | 2 x 2 m | [Snow 02](https://polyhaven.com/a/snow_02) |

## Shared material

| Layer | Material | Use | Repeat size | Source |
| --- | --- | --- | --- | --- |
| Detail | `shared/detail/rocks_ground_02` | Neutral dirt with embedded stones. Use as a transition around paths, exposed ground, and dungeon clearings in either biome. | 2 x 2 m | [Rocks Ground 02](https://polyhaven.com/a/rocks_ground_02) |

## Layering order

Do not render these as a hard stack. Calculate soft masks and blend them, perturbing every threshold with low-frequency noise.

```text
HILLS:      aerial grass/rock macro tint
             + sparse grass on flat open ground
             + forest material under tree-density mask
             + shared rocky soil where grass thins

MOUNTAINS:  rocky-terrain macro tint
             + dark rock on steep slope mask (triplanar)
             + scree below steep rock / in drainage channels
             + lichen in shaded or wet lower zones
             + snow-field macro + snow detail at high elevation
```

Start by sampling the base-colour maps. Add the matching normal and roughness maps together once the terrain shader supports PBR lighting. Use `SamplerState.LinearWrap` (or equivalent address-repeat sampler) for every tileable material.
