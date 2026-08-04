// Data model for the Silent Labyrinth dungeon editor.
// A dungeon is a grid of terrain tiles plus a sparse layer of placed objects
// (one object per cell). This mirrors the bird's-eye tile design described in
// the project proposal (rooms, walls, pits, doors, chests, switches, etc.).

export type TileType = "floor" | "wall" | "pit" | "door";

export type ObjectType =
  | "player" // player spawn (R20)
  | "exit" // level exit (R20)
  | "chest" // regular treasure chest (R13)
  | "finalChest" // final chest that completes the level (R41)
  | "switch" // interactable switch (R13)
  | "npc" // non-player character (R13)
  | "enemy" // stealth enemy to avoid (R31)
  | "lock" // lockpicking puzzle node (R11)
  | "candle" // light source for light puzzles (R12)
  | "mirror" // reflective surface for light puzzles (R12)
  | "key"; // collectable key

export type Biome = "stone" | "cavern" | "crypt" | "ice" | "sand";

export const BIOMES: Biome[] = ["stone", "cavern", "crypt", "ice", "sand"];

export interface PlacedObject {
  type: ObjectType;
  x: number;
  y: number;
}

export interface DungeonMap {
  version: 1;
  name: string;
  biome: Biome;
  width: number;
  height: number;
  // Row-major terrain grid, length === width * height.
  tiles: TileType[];
  // Sparse object layer keyed by "x,y".
  objects: Record<string, PlacedObject>;
}

export function key(x: number, y: number): string {
  return `${x},${y}`;
}

export function createMap(width: number, height: number, biome: Biome): DungeonMap {
  return {
    version: 1,
    name: "Untitled Dungeon",
    biome,
    width,
    height,
    tiles: new Array(width * height).fill("floor" as TileType),
    objects: {},
  };
}
