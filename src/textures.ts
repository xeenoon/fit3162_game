import type { Biome, ObjectType, TileType } from "./types";

// All art is generated procedurally into offscreen canvases — there are no
// external image files. Tiles are drawn at native resolution (TILE px) and the
// editor blits them scaled with image smoothing off for a crisp pixel look.

export const TILE = 32;

// ---- deterministic noise ---------------------------------------------------

function mulberry32(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a |= 0;
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function hashStr(s: string): number {
  let h = 2166136261;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return h >>> 0;
}

// ---- colour helpers --------------------------------------------------------

interface RGB {
  r: number;
  g: number;
  b: number;
}

function hex(c: string): RGB {
  const n = parseInt(c.slice(1), 16);
  return { r: (n >> 16) & 255, g: (n >> 8) & 255, b: n & 255 };
}

function css({ r, g, b }: RGB): string {
  return `rgb(${r | 0},${g | 0},${b | 0})`;
}

function shade(c: RGB, amt: number): RGB {
  // amt > 0 lightens, < 0 darkens.
  const t = amt < 0 ? 0 : 255;
  const k = Math.abs(amt);
  return {
    r: c.r + (t - c.r) * k,
    g: c.g + (t - c.g) * k,
    b: c.b + (t - c.b) * k,
  };
}

// ---- biome palettes --------------------------------------------------------

interface Palette {
  floor: string;
  floorAlt: string;
  wall: string;
  wallMortar: string;
  pit: string;
  accent: string;
}

const PALETTES: Record<Biome, Palette> = {
  stone: { floor: "#6f6f7a", floorAlt: "#5c5c66", wall: "#8a8a96", wallMortar: "#3c3c44", pit: "#101018", accent: "#a9b0c4" },
  cavern: { floor: "#5a4a3a", floorAlt: "#4a3c2e", wall: "#6f5a44", wallMortar: "#2a2018", pit: "#0d0906", accent: "#8a6b3f" },
  crypt: { floor: "#4a4658", floorAlt: "#3c384a", wall: "#5b566e", wallMortar: "#22202c", pit: "#0a0812", accent: "#7d6fa0" },
  ice: { floor: "#9fc6d8", floorAlt: "#8bb6cc", wall: "#c3e2ef", wallMortar: "#5f8aa0", pit: "#0e2634", accent: "#e6f6ff" },
  sand: { floor: "#c9b184", floorAlt: "#b89e6e", wall: "#d9c79b", wallMortar: "#8a6f45", pit: "#241a0e", accent: "#f0e0b0" },
};

// ---- canvas factory --------------------------------------------------------

function make(): { c: HTMLCanvasElement; g: CanvasRenderingContext2D } {
  const c = document.createElement("canvas");
  c.width = TILE;
  c.height = TILE;
  const g = c.getContext("2d")!;
  g.imageSmoothingEnabled = false;
  return { c, g };
}

function px(g: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, color: string): void {
  g.fillStyle = color;
  g.fillRect(x, y, w, h);
}

// ---- terrain tiles ---------------------------------------------------------

function drawFloor(g: CanvasRenderingContext2D, p: Palette, seed: number): void {
  const rnd = mulberry32(seed);
  const base = hex(p.floor);
  const alt = hex(p.floorAlt);
  for (let y = 0; y < TILE; y++) {
    for (let x = 0; x < TILE; x++) {
      const n = rnd();
      const c = n < 0.5 ? base : alt;
      px(g, x, y, 1, 1, css(shade(c, (n - 0.5) * 0.12)));
    }
  }
  // subtle flagstone seams
  px(g, 0, 0, TILE, 1, css(shade(base, -0.25)));
  px(g, 0, 0, 1, TILE, css(shade(base, -0.25)));
  px(g, 15, 0, 1, TILE, css(shade(base, -0.18)));
  px(g, 0, 15, TILE, 1, css(shade(base, -0.18)));
}

function drawWall(g: CanvasRenderingContext2D, p: Palette, seed: number): void {
  const rnd = mulberry32(seed);
  const brick = hex(p.wall);
  px(g, 0, 0, TILE, TILE, p.wallMortar);
  const bh = 8;
  for (let row = 0, y = 0; y < TILE; y += bh, row++) {
    const offset = row % 2 === 0 ? 0 : 8;
    for (let x = -offset; x < TILE; x += 16) {
      const jitter = shade(brick, (rnd() - 0.5) * 0.18);
      px(g, x + 1, y + 1, 14, bh - 2, css(jitter));
      // top highlight + bottom shadow for a bevelled look
      px(g, x + 1, y + 1, 14, 1, css(shade(jitter, 0.18)));
      px(g, x + 1, y + bh - 2, 14, 1, css(shade(jitter, -0.22)));
    }
  }
}

function drawPit(g: CanvasRenderingContext2D, p: Palette): void {
  const edge = hex(p.pit);
  for (let y = 0; y < TILE; y++) {
    for (let x = 0; x < TILE; x++) {
      const dx = (x - 15.5) / 15.5;
      const dy = (y - 15.5) / 15.5;
      const d = Math.min(1, Math.sqrt(dx * dx + dy * dy));
      px(g, x, y, 1, 1, css(shade(edge, (1 - d) * -0.05 + d * 0.12)));
    }
  }
  // rim
  g.strokeStyle = css(shade(hex(p.wall), -0.15));
  g.lineWidth = 2;
  g.strokeRect(1, 1, TILE - 2, TILE - 2);
}

function drawDoor(g: CanvasRenderingContext2D, p: Palette, seed: number): void {
  drawFloor(g, p, seed);
  const frame = hex(p.accent);
  // stone archway frame
  px(g, 2, 0, 4, TILE, css(shade(frame, -0.1)));
  px(g, TILE - 6, 0, 4, TILE, css(shade(frame, -0.1)));
  px(g, 2, 0, TILE - 4, 4, css(shade(frame, -0.1)));
  // wooden planks
  const wood = hex("#6b4a2b");
  px(g, 8, 4, TILE - 16, TILE - 4, css(wood));
  for (let x = 8; x < TILE - 8; x += 5) px(g, x, 4, 1, TILE - 4, css(shade(wood, -0.25)));
  px(g, 8, 14, TILE - 16, 2, css(shade(wood, -0.3)));
}

// ---- object icons (drawn over a transparent tile) --------------------------

function figure(g: CanvasRenderingContext2D, body: string, outline: string): void {
  // simple bird's-eye humanoid
  px(g, 12, 6, 8, 8, outline); // head halo
  px(g, 13, 7, 6, 6, "#e6c9a8"); // head
  px(g, 10, 14, 12, 12, outline); // body outline
  px(g, 11, 15, 10, 10, body); // body
  px(g, 13, 17, 2, 6, css(shade(hex(body), -0.3)));
  px(g, 17, 17, 2, 6, css(shade(hex(body), -0.3)));
}

function drawObject(g: CanvasRenderingContext2D, type: ObjectType): void {
  switch (type) {
    case "player":
      figure(g, "#3fbf6f", "#123");
      break;
    case "npc":
      figure(g, "#4f7fd6", "#123");
      break;
    case "enemy": {
      figure(g, "#c33f3f", "#210");
      // menacing eyes
      px(g, 14, 9, 2, 2, "#ffec6b");
      px(g, 17, 9, 2, 2, "#ffec6b");
      break;
    }
    case "exit": {
      // descending stairs
      const s = hex("#2b2b34");
      for (let i = 0; i < 5; i++) {
        px(g, 5, 6 + i * 4, 22 - i * 3, 4, css(shade(s, 0.05 + i * 0.06)));
      }
      px(g, 4, 4, 24, 24, "rgba(120,200,255,0.0)");
      g.strokeStyle = "#9fdcff";
      g.strokeRect(4, 4, 24, 24);
      break;
    }
    case "chest":
    case "finalChest": {
      const gold = type === "finalChest";
      const wood = hex(gold ? "#c9a13a" : "#7a4f28");
      px(g, 6, 12, 20, 14, css(shade(wood, -0.15))); // base
      px(g, 6, 8, 20, 6, css(wood)); // lid
      px(g, 5, 7, 22, 2, css(shade(wood, 0.2)));
      // bands
      px(g, 6, 18, 20, 2, css(shade(wood, -0.35)));
      px(g, 14, 8, 4, 18, css(shade(wood, -0.35)));
      // lock plate
      px(g, 14, 14, 4, 5, gold ? "#fff2b0" : "#d8c15a");
      px(g, 15, 16, 2, 2, "#3a2a10");
      break;
    }
    case "switch": {
      px(g, 12, 20, 8, 5, "#3a3a44"); // base
      px(g, 15, 8, 2, 12, "#8a8a96"); // shaft
      px(g, 13, 6, 6, 4, "#d64f4f"); // knob
      break;
    }
    case "lock": {
      px(g, 10, 14, 12, 11, "#d8c15a"); // body
      px(g, 11, 15, 10, 9, "#f0dc78");
      g.strokeStyle = "#8a6f20";
      g.lineWidth = 2;
      g.beginPath();
      g.arc(16, 14, 5, Math.PI, 0); // shackle
      g.stroke();
      px(g, 15, 18, 2, 4, "#8a6f20"); // keyhole
      px(g, 14, 18, 4, 2, "#8a6f20");
      break;
    }
    case "key": {
      px(g, 9, 15, 8, 3, "#e6c34a"); // shaft
      g.strokeStyle = "#e6c34a";
      g.lineWidth = 3;
      g.beginPath();
      g.arc(21, 16, 4, 0, Math.PI * 2); // bow
      g.stroke();
      px(g, 10, 18, 2, 3, "#e6c34a"); // teeth
      px(g, 13, 18, 2, 3, "#e6c34a");
      break;
    }
    case "candle": {
      px(g, 12, 16, 8, 10, "#e8e2d0"); // wax
      px(g, 12, 16, 8, 2, "#fff");
      px(g, 15, 8, 2, 8, "#3a2a10"); // wick shadow
      // flame
      px(g, 14, 8, 4, 6, "#ffb43a");
      px(g, 15, 7, 2, 5, "#ffe97a");
      // soft glow
      const grad = g.createRadialGradient(16, 10, 1, 16, 10, 14);
      grad.addColorStop(0, "rgba(255,220,120,0.5)");
      grad.addColorStop(1, "rgba(255,220,120,0)");
      g.fillStyle = grad;
      g.fillRect(0, 0, TILE, TILE);
      break;
    }
    case "mirror": {
      g.save();
      g.translate(16, 16);
      g.rotate(-Math.PI / 4);
      px(g, -12, -3, 24, 6, "#2a3a44"); // frame
      px(g, -11, -2, 22, 3, "#bfe9ff"); // reflective face
      px(g, -11, 1, 22, 1, "#6f9fb0");
      g.restore();
      break;
    }
  }
}

// ---- caches ----------------------------------------------------------------

const tileCache = new Map<string, HTMLCanvasElement>();
const objectCache = new Map<ObjectType, HTMLCanvasElement>();

export function tileTexture(type: TileType, biome: Biome): HTMLCanvasElement {
  const id = `${type}:${biome}`;
  const cached = tileCache.get(id);
  if (cached) return cached;
  const { c, g } = make();
  const p = PALETTES[biome];
  const seed = hashStr(id);
  if (type === "floor") drawFloor(g, p, seed);
  else if (type === "wall") drawWall(g, p, seed);
  else if (type === "pit") drawPit(g, p);
  else drawDoor(g, p, seed);
  tileCache.set(id, c);
  return c;
}

export function objectTexture(type: ObjectType): HTMLCanvasElement {
  const cached = objectCache.get(type);
  if (cached) return cached;
  const { c, g } = make();
  drawObject(g, type);
  objectCache.set(type, c);
  return c;
}

// A small preview swatch for the palette (fixed biome so icons read clearly).
export function swatch(entry: { kind: string; type: string }, biome: Biome): HTMLCanvasElement {
  if (entry.kind === "tile") return tileTexture(entry.type as TileType, biome);
  if (entry.kind === "object") return objectTextureOnFloor(entry.type as ObjectType, biome);
  // tools: draw a glyph
  const { c, g } = make();
  px(g, 0, 0, TILE, TILE, "rgba(255,255,255,0.04)");
  g.fillStyle = "#cdd3e0";
  g.font = "18px sans-serif";
  g.textAlign = "center";
  g.textBaseline = "middle";
  g.fillText(entry.type === "erase" ? "⌫" : "✥", TILE / 2, TILE / 2 + 1);
  return c;
}

function objectTextureOnFloor(type: ObjectType, biome: Biome): HTMLCanvasElement {
  const id = `obj-on-floor:${type}:${biome}`;
  const cached = tileCache.get(id);
  if (cached) return cached;
  const { c, g } = make();
  g.drawImage(tileTexture("floor", biome), 0, 0);
  g.drawImage(objectTexture(type), 0, 0);
  tileCache.set(id, c);
  return c;
}
