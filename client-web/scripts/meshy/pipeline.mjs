// Shared Meshy steps: every created task id is recorded first, so an interrupted run resumes without paying twice.
import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, rmSync } from "node:fs";
import { writeFile } from "node:fs/promises";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";

export const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
export const PUBLIC = join(ROOT, "public");
export const MODELS = join(PUBLIC, "models");
const MANIFEST = join(MODELS, "manifest.json");
const STATE_FILE = join(ROOT, "scripts", "meshy", "tasks.json");

export const TEXT_TO_3D = "/openapi/v2/text-to-3d";
export const RETEXTURE = "/openapi/v1/retexture";
const RIGGING = "/openapi/v1/rigging";
const ANIMATIONS = "/openapi/v1/animations";
/** Low enough for dozens of characters on screen in a browser, high enough to keep faces readable. */
const TARGET_POLYCOUNT = 12000;
/** Smart Topology builds a clean game mesh at the target face count, which rigging needs. */
const GEOMETRY_MODEL = "meshy-t2.5";
const MAX_PROMPT = 800;
const TEXTURE_JPEG_QUALITY = "85";

/** Slots the client plays; each picks the first library animation whose key matches. */
const CLIP_SLOTS = {
  biped: {
    idle: [/^Idle$/i, /idle/i],
    walk: [/^Walking$/i, /^walk/i],
    run: [/^Running$/i, /^run/i],
    attack: [/^Attack$/i, /attack/i, /slash/i],
    cast: [/cast/i, /spell/i, /magic/i],
    hit: [/hit_?react/i, /^hit/i, /impact/i, /damage/i],
    death: [/^Dead$/i, /death/i, /dying/i, /^die/i],
    cheer: [/cheer/i, /victory/i, /celebrat/i],
  },
  quadruped: {
    idle: [/idle/i, /stand/i],
    walk: [/walk/i],
    run: [/run/i, /gallop/i],
    attack: [/attack/i, /bite/i],
    hit: [/hit/i, /damage/i],
    death: [/death/i, /die/i, /dead/i],
  },
};

function readJson(path, fallback) {
  return existsSync(path) ? JSON.parse(readFileSync(path, "utf8")) : fallback;
}

export function publicPath(absolutePath) {
  return relative(PUBLIC, absolutePath);
}

/** Serialises writes to the shared task state and manifest from parallel workers. */
export class Store {
  constructor() {
    mkdirSync(MODELS, { recursive: true });
    this.tasks = readJson(STATE_FILE, {});
    this.manifest = { assets: {}, kits: {}, ...readJson(MANIFEST, {}) };
    this.writing = Promise.resolve();
  }

  record(key, patch) {
    this.tasks[key] = { ...this.tasks[key], ...patch };
    return this.flush();
  }

  /** A failed task must be recreated on the next run rather than polled again. */
  forget(key, taskId) {
    const tasks = this.tasks[key] ?? {};
    const field = Object.keys(tasks).find((name) => tasks[name] === taskId);
    if (field) {
      delete tasks[field];
    }
    return this.flush();
  }

  publish(key, entry) {
    this.manifest.assets[key] = entry;
    return this.flush();
  }

  /** Sets manifest.kits[raceId][...path] = value. */
  publishKit(raceId, path, value) {
    let node = (this.manifest.kits[raceId] ??= {});
    for (const segment of path.slice(0, -1)) {
      node = node[segment] ??= {};
    }
    node[path.at(-1)] = value;
    return this.flush();
  }

  unpublishKit(raceId, path) {
    const parent = path.slice(0, -1).reduce((node, segment) => node?.[segment], this.manifest.kits[raceId]);
    if (parent) {
      delete parent[path.at(-1)];
    }
  }

  flush() {
    this.writing = this.writing.then(() => Promise.all([
      writeFile(STATE_FILE, JSON.stringify(this.tasks, null, 2) + "\n"),
      writeFile(MANIFEST, JSON.stringify(this.manifest, null, 2) + "\n"),
    ]));
    return this.writing;
  }
}

/** One Meshy job of one asset: task ids live under store.tasks[key][field]. */
export class AssetJob {
  constructor(client, store, key, log) {
    Object.assign(this, { client, store, key, log });
  }

  async step(field, create) {
    const existing = this.store.tasks[this.key]?.[field];
    if (existing) {
      return existing;
    }
    const taskId = await create();
    await this.store.record(this.key, { [field]: taskId });
    return taskId;
  }

  async wait(path, taskId, label) {
    try {
      return await this.client.waitFor(path, taskId, (progress) => this.log(`${label} ${progress}%`));
    } catch (error) {
      await this.store.forget(this.key, error.failedTaskId);
      throw error;
    }
  }

  /** Smart-topology preview, then a textured refine. Returns the refine task id and the finished task. */
  async textTo3d({ prompt, texturePrompt = prompt, pose }) {
    const preview = await this.step("preview", () => this.client.create(TEXT_TO_3D, {
      mode: "preview",
      prompt: prompt.slice(0, MAX_PROMPT),
      model_type: "smart-topology",
      ai_model: GEOMETRY_MODEL,
      target_polycount: TARGET_POLYCOUNT,
      ...(pose ? { pose_mode: pose } : {}),
      target_formats: ["glb"],
    }));
    await this.wait(TEXT_TO_3D, preview, "меш");
    const refine = await this.step("refine", () => this.client.create(TEXT_TO_3D, {
      mode: "refine",
      preview_task_id: preview,
      texture_prompt: texturePrompt.slice(0, MAX_PROMPT),
      target_formats: ["glb"],
    }));
    return { refine, task: await this.wait(TEXT_TO_3D, refine, "текстура") };
  }

  /** Rigs a textured task, downloads the rigged GLB and a merged GLB of the slot clips. */
  async rigAndAnimate({ inputTask, rig, height, library, modelPath, animationPath }) {
    const rigTask = await this.step("rig", () => this.client.create(RIGGING, {
      input_task_id: inputTask,
      animation_type: rig,
      ...(rig === "biped" ? { height_meters: height } : {}),
    }));
    const rigged = await this.wait(RIGGING, rigTask, "скелет");
    await this.client.download(rigged.result.rigged_character_glb_url, modelPath);
    const entry = { model: publicPath(modelPath), rig, height, clips: {} };
    const clips = pickClips(library, rig);
    if (Object.keys(clips).length === 0) {
      return entry;
    }
    const animation = await this.step("animation", () => this.client.create(ANIMATIONS, {
      rig_task_id: rigTask,
      action_ids: Object.values(clips).map((clip) => clip.action_id),
    }));
    const animated = await this.wait(ANIMATIONS, animation, "анимации");
    await this.client.download(animated.result.animation_glb_url, animationPath);
    entry.animations = publicPath(animationPath);
    entry.clips = Object.fromEntries(Object.entries(clips).map(([slot, clip]) => [slot, clip.name]));
    return entry;
  }

  /** Repaints a textured task on its original UVs and saves only the base color texture. */
  async retexture({ inputTask, prompt, texturePath }) {
    const task = await this.step("retexture", () => this.client.create(RETEXTURE, {
      input_task_id: inputTask,
      text_style_prompt: prompt.slice(0, MAX_PROMPT),
      enable_original_uv: true,
      target_formats: ["glb"],
    }));
    const finished = await this.wait(RETEXTURE, task, "текстура");
    await downloadTexture(this.client, finished.texture_urls[0].base_color, texturePath);
    return publicPath(texturePath);
  }
}

/** Meshy textures are 2K PNGs; JPEG keeps the download small. Uses macOS sips, which the art machine has. */
async function downloadTexture(client, url, jpegPath) {
  const pngPath = jpegPath.replace(/\.jpg$/, ".png");
  await client.download(url, pngPath);
  try {
    execFileSync("sips", ["-s", "format", "jpeg", "-s", "formatOptions", TEXTURE_JPEG_QUALITY, pngPath, "--out", jpegPath], { stdio: "ignore" });
  } catch {
    throw new Error(`не удалось сконвертировать ${pngPath} в JPEG (нужен sips из macOS)`);
  }
  rmSync(pngPath);
}

export function pickClips(library, rig) {
  const slots = CLIP_SLOTS[rig] ?? {};
  const available = library.filter((animation) => animation.rig_type === rig);
  const picked = {};
  for (const [slot, patterns] of Object.entries(slots)) {
    const match = patterns.map((pattern) => available.find((animation) => pattern.test(animation.key))).find(Boolean);
    if (match && !Object.values(picked).some((entry) => entry.action_id === match.action_id)) {
      picked[slot] = match;
    }
  }
  return picked;
}

export async function runPool(items, concurrency, worker) {
  const queue = [...items];
  await Promise.all(Array.from({ length: Math.max(1, concurrency) }, async () => {
    for (let item = queue.shift(); item; item = queue.shift()) {
      await worker(item);
    }
  }));
}

/** Runs jobs in parallel with per-asset logs; returns the keys that failed. */
export async function runJobs(client, store, items, concurrency, run) {
  const failures = [];
  await runPool(items, concurrency, async (item) => {
    let lastLine = "";
    const job = new AssetJob(client, store, item.key, (text) => {
      if (text !== lastLine) {
        lastLine = text;
        console.log(`${item.key}: ${text}`);
      }
    });
    try {
      await run(job, item);
      console.log(`${item.key}: готово`);
    } catch (error) {
      failures.push(item.key);
      console.error(`${item.key}: ошибка — ${error.message}`);
    }
  });
  await store.flush();
  return failures;
}

export function modelsPath(...segments) {
  return join(MODELS, ...segments);
}
