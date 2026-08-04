import "./style.css";
import { Editor } from "./editor";
import { swatch } from "./textures";
import { OBJECTS, TILES, TOOLS, type PaletteEntry } from "./tools";
import { BIOMES } from "./types";

const canvas = document.getElementById("stage") as HTMLCanvasElement;

const initial: PaletteEntry = TILES[1]; // wall — a sensible default brush
const editor = new Editor(canvas, initial);

// ---- palette ---------------------------------------------------------------

const buttons: HTMLButtonElement[] = [];

function selectEntry(entry: PaletteEntry, btn: HTMLButtonElement): void {
  editor.setTool(entry);
  for (const b of buttons) b.classList.toggle("active", b === btn);
  toolLabel.textContent = entry.label;
  toolHint.textContent = entry.hint;
}

function buildPalette(container: HTMLElement, entries: PaletteEntry[]): void {
  for (const entry of entries) {
    const btn = document.createElement("button");
    btn.className = "cell";
    btn.title = `${entry.label} — ${entry.hint}`;

    const img = swatch(entry, editor.getBiome());
    img.className = "cell-img";
    btn.appendChild(img);

    const cap = document.createElement("span");
    cap.textContent = entry.label;
    btn.appendChild(cap);

    btn.addEventListener("click", () => selectEntry(entry, btn));
    container.appendChild(btn);
    buttons.push(btn);
    swatchRefs.push({ entry, img, btn });
  }
}

const swatchRefs: { entry: PaletteEntry; img: HTMLCanvasElement; btn: HTMLButtonElement }[] = [];

const toolLabel = document.getElementById("tool")!;
const toolHint = document.getElementById("toolHint")!;

buildPalette(document.getElementById("tilePalette")!, TILES);
buildPalette(document.getElementById("objectPalette")!, OBJECTS);
buildPalette(document.getElementById("toolPalette")!, TOOLS);

// activate the default brush
const defaultBtn = swatchRefs.find((r) => r.entry === initial)!.btn;
selectEntry(initial, defaultBtn);

// ---- biome selector --------------------------------------------------------

const biomeSelect = document.getElementById("biome") as HTMLSelectElement;
for (const b of BIOMES) {
  const opt = document.createElement("option");
  opt.value = b;
  opt.textContent = b[0].toUpperCase() + b.slice(1);
  biomeSelect.appendChild(opt);
}
biomeSelect.value = editor.getBiome();
biomeSelect.addEventListener("change", () => {
  editor.setBiome(biomeSelect.value as (typeof BIOMES)[number]);
  refreshSwatches();
});

function refreshSwatches(): void {
  // Re-render palette swatches that depend on biome (tiles + objects-on-floor).
  const biome = editor.getBiome();
  for (const ref of swatchRefs) {
    if (ref.entry.kind === "tool") continue;
    const fresh = swatch(ref.entry, biome);
    fresh.className = "cell-img";
    ref.img.replaceWith(fresh);
    ref.img = fresh;
  }
}

// ---- toolbar buttons -------------------------------------------------------

const gridW = document.getElementById("gridW") as HTMLInputElement;
const gridH = document.getElementById("gridH") as HTMLInputElement;
const size = editor.getSize();
gridW.value = String(size.width);
gridH.value = String(size.height);

document.getElementById("resize")!.addEventListener("click", () => {
  editor.resizeGrid(Number(gridW.value), Number(gridH.value));
  const s = editor.getSize();
  gridW.value = String(s.width);
  gridH.value = String(s.height);
});

document.getElementById("clear")!.addEventListener("click", () => {
  if (confirm("Clear the whole dungeon? This cannot be undone.")) editor.clear();
});

document.getElementById("save")!.addEventListener("click", () => {
  const blob = new Blob([editor.toJSON()], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = "dungeon.json";
  a.click();
  URL.revokeObjectURL(url);
});

const fileInput = document.getElementById("fileInput") as HTMLInputElement;
document.getElementById("load")!.addEventListener("click", () => fileInput.click());
fileInput.addEventListener("change", async () => {
  const file = fileInput.files?.[0];
  if (!file) return;
  try {
    editor.loadJSON(await file.text());
    biomeSelect.value = editor.getBiome();
    const s = editor.getSize();
    gridW.value = String(s.width);
    gridH.value = String(s.height);
    refreshSwatches();
  } catch (err) {
    alert(`Could not load file: ${(err as Error).message}`);
  }
  fileInput.value = "";
});

// ---- status bar ------------------------------------------------------------

const coords = document.getElementById("coords")!;
editor.onStatus = (p) => {
  coords.textContent = p.inside ? `x: ${p.cx}, y: ${p.cy}` : "—";
};
