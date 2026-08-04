# Silent Labyrinth — Dungeon Editor

A basic TypeScript web app for designing the top-down dungeons described in the
*Silent Labyrinth* project proposal. You paint terrain, drop objects, and drag
pieces around to lay out a level, then save it to JSON.

Everything visual is **generated procedurally in code** — there are no external
image files. All tile and object textures are drawn onto offscreen canvases in
`src/textures.ts` (pixel-art style, recoloured per biome).

> Scope note: this is *only* the editor, not the game itself. It produces the
> dungeon data the game would later load.

## Run

```bash
npm install
npm run dev      # start the editor at http://localhost:5173
npm run build    # typecheck + production build into dist/
```

## Using it

- **Terrain** (floor / wall / pit / door) — left-drag to paint, right-drag to erase.
- **Objects** (spawn, exit, chests, keys, locks, switches, NPCs, enemies,
  candles, mirrors) — click a cell to place. Spawn / Exit / Final Chest are
  unique and move when re-placed.
- **Tools**
  - *Erase* — remove an object, or reset a tile to floor.
  - *Move* — drag an already-placed object to a new cell ("move bits around").
- **Biome** — restyle every tile (Stone, Cavern, Crypt, Ice, Sand) to match the
  different dungeon themes.
- **Resize** — change the grid dimensions (existing content is preserved).
- **Save JSON / Load JSON** — export or import a dungeon. Work is also
  auto-saved to `localStorage` between sessions.

Canvas navigation: **scroll** to zoom, **middle-drag** or **Space + drag** to pan.

## Saved map format

```jsonc
{
  "version": 1,
  "name": "Untitled Dungeon",
  "biome": "stone",
  "width": 24,
  "height": 16,
  "tiles": ["floor", "wall", ...],        // row-major, width * height entries
  "objects": {                             // sparse, keyed by "x,y"
    "3,5": { "type": "player", "x": 3, "y": 5 }
  }
}
```

## Source layout

| File | Responsibility |
|------|----------------|
| `src/types.ts` | Dungeon data model (tiles, objects, biomes) |
| `src/tools.ts` | Palette definitions shown in the sidebar |
| `src/textures.ts` | Procedural pixel-art texture generation |
| `src/editor.ts` | Canvas rendering, input, painting, drag, save/load |
| `src/main.ts` | UI wiring (palette, biome, toolbar buttons) |
