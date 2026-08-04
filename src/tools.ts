import type { ObjectType, TileType } from "./types";

// Palette metadata: what shows up in the left sidebar and how each entry reads.

export interface TileEntry {
  kind: "tile";
  type: TileType;
  label: string;
  hint: string;
}

export interface ObjectEntry {
  kind: "object";
  type: ObjectType;
  label: string;
  hint: string;
  // Objects with `unique: true` may only exist once on the map (spawn / exit).
  unique?: boolean;
}

export interface ToolEntry {
  kind: "tool";
  type: "erase" | "move";
  label: string;
  hint: string;
}

export type PaletteEntry = TileEntry | ObjectEntry | ToolEntry;

export const TILES: TileEntry[] = [
  { kind: "tile", type: "floor", label: "Floor", hint: "Walkable ground." },
  { kind: "tile", type: "wall", label: "Wall", hint: "Solid, blocks movement and sight." },
  { kind: "tile", type: "pit", label: "Pit", hint: "Falling in triggers Game Over (R31)." },
  { kind: "tile", type: "door", label: "Door", hint: "Room transition / threshold." },
];

export const OBJECTS: ObjectEntry[] = [
  { kind: "object", type: "player", label: "Spawn", hint: "Player start position (R20).", unique: true },
  { kind: "object", type: "exit", label: "Exit", hint: "Reach here to leave the level (R20).", unique: true },
  { kind: "object", type: "finalChest", label: "Final Chest", hint: "Completes the level (R41).", unique: true },
  { kind: "object", type: "chest", label: "Chest", hint: "Treasure / loot chest." },
  { kind: "object", type: "key", label: "Key", hint: "Collectable key for locks." },
  { kind: "object", type: "lock", label: "Lock", hint: "Lockpicking puzzle node (R11)." },
  { kind: "object", type: "switch", label: "Switch", hint: "Interactable switch, E key (R13)." },
  { kind: "object", type: "npc", label: "NPC", hint: "Non-player character (R13)." },
  { kind: "object", type: "enemy", label: "Enemy", hint: "Avoid via stealth (R31)." },
  { kind: "object", type: "candle", label: "Candle", hint: "Light source for light puzzles (R12)." },
  { kind: "object", type: "mirror", label: "Mirror", hint: "Reflects light beams (R12)." },
];

export const TOOLS: ToolEntry[] = [
  { kind: "tool", type: "erase", label: "Erase", hint: "Remove object, or reset tile to floor." },
  { kind: "tool", type: "move", label: "Move", hint: "Drag a placed object to a new cell." },
];

export function labelFor(entry: PaletteEntry): string {
  return entry.label;
}
