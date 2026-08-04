import { createMap, key, type Biome, type DungeonMap, type ObjectType, type PlacedObject, type TileType } from "./types";
import { objectTexture, TILE, tileTexture } from "./textures";
import type { PaletteEntry } from "./tools";

const STORAGE_KEY = "silent-labyrinth-map";

interface Pointer {
  cx: number; // cell x
  cy: number; // cell y
  inside: boolean;
}

export class Editor {
  private canvas: HTMLCanvasElement;
  private ctx: CanvasRenderingContext2D;
  private map: DungeonMap;

  private scale = 30; // rendered pixels per tile
  private offsetX = 40;
  private offsetY = 40;

  private current: PaletteEntry;
  private pointer: Pointer = { cx: -1, cy: -1, inside: false };

  private mode: "idle" | "paint" | "erase" | "pan" | "drag" = "idle";
  private panStart = { x: 0, y: 0, ox: 0, oy: 0 };
  private spaceDown = false;
  private dragging: PlacedObject | null = null;

  private dirty = true;
  onStatus: (p: Pointer) => void = () => {};

  constructor(canvas: HTMLCanvasElement, current: PaletteEntry) {
    this.canvas = canvas;
    this.ctx = canvas.getContext("2d")!;
    this.current = current;
    this.map = this.restore() ?? createMap(24, 16, "stone");

    this.bind();
    this.resizeToParent();
    window.addEventListener("resize", () => this.resizeToParent());
    requestAnimationFrame(this.loop);
  }

  // ---- public API ----------------------------------------------------------

  setTool(entry: PaletteEntry): void {
    this.current = entry;
  }

  getBiome(): Biome {
    return this.map.biome;
  }

  setBiome(biome: Biome): void {
    this.map.biome = biome;
    this.markDirty();
    this.persist();
  }

  getSize(): { width: number; height: number } {
    return { width: this.map.width, height: this.map.height };
  }

  resizeGrid(width: number, height: number): void {
    width = clamp(width, 4, 128);
    height = clamp(height, 4, 128);
    const tiles: TileType[] = new Array(width * height).fill("floor");
    for (let y = 0; y < Math.min(height, this.map.height); y++) {
      for (let x = 0; x < Math.min(width, this.map.width); x++) {
        tiles[y * width + x] = this.map.tiles[y * this.map.width + x];
      }
    }
    const objects: Record<string, PlacedObject> = {};
    for (const o of Object.values(this.map.objects)) {
      if (o.x < width && o.y < height) objects[key(o.x, o.y)] = o;
    }
    this.map = { ...this.map, width, height, tiles, objects };
    this.markDirty();
    this.persist();
  }

  clear(): void {
    this.map.tiles.fill("floor");
    this.map.objects = {};
    this.markDirty();
    this.persist();
  }

  toJSON(): string {
    return JSON.stringify(this.map, null, 2);
  }

  loadJSON(text: string): void {
    const data = JSON.parse(text) as DungeonMap;
    if (!data || data.version !== 1 || !Array.isArray(data.tiles)) {
      throw new Error("Not a valid dungeon file.");
    }
    this.map = data;
    this.markDirty();
    this.persist();
  }

  // ---- persistence ---------------------------------------------------------

  private persist(): void {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(this.map));
    } catch {
      /* storage may be unavailable; ignore */
    }
  }

  private restore(): DungeonMap | null {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const data = JSON.parse(raw) as DungeonMap;
      return data.version === 1 ? data : null;
    } catch {
      return null;
    }
  }

  // ---- grid helpers --------------------------------------------------------

  private tileAt(x: number, y: number): TileType {
    return this.map.tiles[y * this.map.width + x];
  }

  private setTile(x: number, y: number, t: TileType): void {
    this.map.tiles[y * this.map.width + x] = t;
  }

  private inBounds(x: number, y: number): boolean {
    return x >= 0 && y >= 0 && x < this.map.width && y < this.map.height;
  }

  private cellFromEvent(e: MouseEvent): { x: number; y: number } {
    const rect = this.canvas.getBoundingClientRect();
    const px = e.clientX - rect.left - this.offsetX;
    const py = e.clientY - rect.top - this.offsetY;
    return { x: Math.floor(px / this.scale), y: Math.floor(py / this.scale) };
  }

  // ---- editing actions -----------------------------------------------------

  private apply(x: number, y: number): void {
    if (!this.inBounds(x, y)) return;
    const cur = this.current;
    if (cur.kind === "tile") {
      this.setTile(x, y, cur.type);
    } else if (cur.kind === "object") {
      this.placeObject(cur.type, cur.unique === true, x, y);
    }
    this.markDirty();
  }

  private placeObject(type: ObjectType, unique: boolean, x: number, y: number): void {
    if (unique) {
      for (const k of Object.keys(this.map.objects)) {
        if (this.map.objects[k].type === type) delete this.map.objects[k];
      }
    }
    this.map.objects[key(x, y)] = { type, x, y };
  }

  private eraseAt(x: number, y: number): void {
    if (!this.inBounds(x, y)) return;
    const k = key(x, y);
    if (this.map.objects[k]) {
      delete this.map.objects[k];
    } else {
      this.setTile(x, y, "floor");
    }
    this.markDirty();
  }

  // ---- input ---------------------------------------------------------------

  private bind(): void {
    this.canvas.addEventListener("contextmenu", (e) => e.preventDefault());
    this.canvas.addEventListener("mousedown", this.onDown);
    window.addEventListener("mousemove", this.onMove);
    window.addEventListener("mouseup", this.onUp);
    this.canvas.addEventListener("wheel", this.onWheel, { passive: false });
    window.addEventListener("keydown", (e) => {
      if (e.code === "Space") this.spaceDown = true;
    });
    window.addEventListener("keyup", (e) => {
      if (e.code === "Space") this.spaceDown = false;
    });
    this.canvas.addEventListener("mouseleave", () => {
      this.pointer.inside = false;
      this.markDirty();
    });
  }

  private onDown = (e: MouseEvent): void => {
    const { x, y } = this.cellFromEvent(e);
    const pan = e.button === 1 || (e.button === 0 && this.spaceDown);
    if (pan) {
      this.mode = "pan";
      this.panStart = { x: e.clientX, y: e.clientY, ox: this.offsetX, oy: this.offsetY };
      return;
    }
    if (e.button === 2) {
      this.mode = "erase";
      this.eraseAt(x, y);
      return;
    }
    if (e.button !== 0) return;

    if (this.current.kind === "tool" && this.current.type === "erase") {
      this.mode = "erase";
      this.eraseAt(x, y);
      return;
    }
    if (this.current.kind === "tool" && this.current.type === "move") {
      const obj = this.inBounds(x, y) ? this.map.objects[key(x, y)] : undefined;
      if (obj) {
        this.dragging = obj;
        delete this.map.objects[key(x, y)];
        this.mode = "drag";
        this.markDirty();
      }
      return;
    }
    this.mode = "paint";
    this.apply(x, y);
  };

  private onMove = (e: MouseEvent): void => {
    const { x, y } = this.cellFromEvent(e);
    const changed = x !== this.pointer.cx || y !== this.pointer.cy;
    this.pointer = { cx: x, cy: y, inside: this.inBounds(x, y) };
    this.onStatus(this.pointer);

    if (this.mode === "pan") {
      this.offsetX = this.panStart.ox + (e.clientX - this.panStart.x);
      this.offsetY = this.panStart.oy + (e.clientY - this.panStart.y);
      this.markDirty();
      return;
    }
    if (!changed) {
      if (this.mode === "drag") this.markDirty();
      return;
    }
    if (this.mode === "paint") this.apply(x, y);
    else if (this.mode === "erase") this.eraseAt(x, y);
    else this.markDirty();
  };

  private onUp = (e: MouseEvent): void => {
    if (this.mode === "drag" && this.dragging) {
      const { x, y } = this.cellFromEvent(e);
      const target = this.inBounds(x, y) && !this.map.objects[key(x, y)] ? { x, y } : { x: this.dragging.x, y: this.dragging.y };
      const dropped = { ...this.dragging, x: target.x, y: target.y };
      this.map.objects[key(target.x, target.y)] = dropped;
      this.dragging = null;
      this.markDirty();
    }
    if (this.mode !== "idle") this.persist();
    this.mode = "idle";
  };

  private onWheel = (e: WheelEvent): void => {
    e.preventDefault();
    const rect = this.canvas.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;
    const worldX = (mx - this.offsetX) / this.scale;
    const worldY = (my - this.offsetY) / this.scale;
    const factor = e.deltaY < 0 ? 1.1 : 1 / 1.1;
    this.scale = clamp(this.scale * factor, 8, 96);
    this.offsetX = mx - worldX * this.scale;
    this.offsetY = my - worldY * this.scale;
    this.markDirty();
  };

  // ---- rendering -----------------------------------------------------------

  private resizeToParent(): void {
    const parent = this.canvas.parentElement!;
    const dpr = window.devicePixelRatio || 1;
    const w = parent.clientWidth;
    const h = parent.clientHeight;
    this.canvas.width = Math.floor(w * dpr);
    this.canvas.height = Math.floor(h * dpr);
    this.canvas.style.width = `${w}px`;
    this.canvas.style.height = `${h}px`;
    this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    this.ctx.imageSmoothingEnabled = false;
    this.markDirty();
  }

  private markDirty(): void {
    this.dirty = true;
  }

  private loop = (): void => {
    if (this.dirty) {
      this.render();
      this.dirty = false;
    }
    requestAnimationFrame(this.loop);
  };

  private render(): void {
    const ctx = this.ctx;
    const w = this.canvas.clientWidth;
    const h = this.canvas.clientHeight;
    ctx.imageSmoothingEnabled = false;

    // backdrop
    ctx.fillStyle = "#0f1117";
    ctx.fillRect(0, 0, w, h);

    const s = this.scale;
    const { width, height, biome } = this.map;

    // only draw visible cells
    const minX = Math.max(0, Math.floor(-this.offsetX / s));
    const minY = Math.max(0, Math.floor(-this.offsetY / s));
    const maxX = Math.min(width, Math.ceil((w - this.offsetX) / s));
    const maxY = Math.min(height, Math.ceil((h - this.offsetY) / s));

    for (let y = minY; y < maxY; y++) {
      for (let x = minX; x < maxX; x++) {
        const dx = Math.round(this.offsetX + x * s);
        const dy = Math.round(this.offsetY + y * s);
        const size = Math.ceil(s);
        ctx.drawImage(tileTexture(this.tileAt(x, y), biome), 0, 0, TILE, TILE, dx, dy, size, size);
      }
    }

    // objects
    for (const o of Object.values(this.map.objects)) {
      if (o.x < minX || o.x >= maxX || o.y < minY || o.y >= maxY) continue;
      const dx = Math.round(this.offsetX + o.x * s);
      const dy = Math.round(this.offsetY + o.y * s);
      ctx.drawImage(objectTexture(o.type), 0, 0, TILE, TILE, dx, dy, Math.ceil(s), Math.ceil(s));
    }

    // grid lines
    ctx.strokeStyle = "rgba(255,255,255,0.06)";
    ctx.lineWidth = 1;
    ctx.beginPath();
    for (let x = minX; x <= maxX; x++) {
      const dx = Math.round(this.offsetX + x * s) + 0.5;
      ctx.moveTo(dx, this.offsetY + minY * s);
      ctx.lineTo(dx, this.offsetY + maxY * s);
    }
    for (let y = minY; y <= maxY; y++) {
      const dy = Math.round(this.offsetY + y * s) + 0.5;
      ctx.moveTo(this.offsetX + minX * s, dy);
      ctx.lineTo(this.offsetX + maxX * s, dy);
    }
    ctx.stroke();

    // map border
    ctx.strokeStyle = "rgba(160,180,255,0.5)";
    ctx.lineWidth = 2;
    ctx.strokeRect(this.offsetX + 0.5, this.offsetY + 0.5, width * s, height * s);

    // hover highlight + ghost of the current tool
    if (this.pointer.inside) {
      const dx = this.offsetX + this.pointer.cx * s;
      const dy = this.offsetY + this.pointer.cy * s;
      ctx.save();
      if (this.mode === "drag" && this.dragging) {
        ctx.globalAlpha = 0.7;
        ctx.drawImage(objectTexture(this.dragging.type), 0, 0, TILE, TILE, Math.round(dx), Math.round(dy), Math.ceil(s), Math.ceil(s));
      } else if (this.current.kind === "object") {
        ctx.globalAlpha = 0.45;
        ctx.drawImage(objectTexture(this.current.type), 0, 0, TILE, TILE, Math.round(dx), Math.round(dy), Math.ceil(s), Math.ceil(s));
      } else if (this.current.kind === "tile") {
        ctx.globalAlpha = 0.5;
        ctx.drawImage(tileTexture(this.current.type, biome), 0, 0, TILE, TILE, Math.round(dx), Math.round(dy), Math.ceil(s), Math.ceil(s));
      }
      ctx.restore();
      ctx.strokeStyle = "#e8f0ff";
      ctx.lineWidth = 2;
      ctx.strokeRect(dx + 1, dy + 1, s - 2, s - 2);
    }
  }
}

function clamp(v: number, lo: number, hi: number): number {
  return Math.max(lo, Math.min(hi, v));
}
