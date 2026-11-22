// Generates character and creature models with Meshy: text → mesh → texture → rig → animations,
// then downloads GLBs into public/models and writes public/models/manifest.json for the client.
// Resumable: task ids are kept in scripts/meshy/tasks.json, so a rerun only finishes what is missing.
//
// Usage:
//   node scripts/meshy-generate.mjs --dry-run                 # list assets and task counts, spends nothing
//   node scripts/meshy-generate.mjs --only race:elf,mob:wolf  # a few assets
//   node scripts/meshy-generate.mjs --group mob               # all mobs
//   node scripts/meshy-generate.mjs                           # everything
// Needs MESHY_API_KEY in the environment or in client-web/.env.local (paid Meshy plan; tasks spend credits).
import { existsSync, mkdirSync, readFileSync } from "node:fs";
import { writeFile } from "node:fs/promises";
import { join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { MeshyClient, loadApiKey } from "./meshy/api.mjs";
import { assetList } from "./meshy/assets.mjs";

const ROOT = fileURLToPath(new URL("..", import.meta.url));
const PUBLIC = join(ROOT, "public");
const MODELS = join(PUBLIC, "models");
const MANIFEST = join(MODELS, "manifest.json");
const STATE_FILE = join(ROOT, "scripts", "meshy", "tasks.json");
const TEXT_TO_3D = "/openapi/v2/text-to-3d";
const RIGGING = "/openapi/v1/rigging";
const ANIMATIONS = "/openapi/v1/animations";
/** Low enough for dozens of characters on screen in a browser, high enough to keep faces readable. */
const TARGET_POLYCOUNT = 12000;
/** Smart Topology builds a clean game mesh at the target face count, which rigging needs. */
const GEOMETRY_MODEL = "meshy-t2.5";
const MAX_TEXTURE_PROMPT = 800;
const DEFAULT_CONCURRENCY = 2;

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

function parseArgs(argv) {
  const value = (flag) => {
    const index = argv.indexOf(flag);
    return index >= 0 ? argv[index + 1] : undefined;
  };
  return {
    dryRun: argv.includes("--dry-run"),
    only: value("--only")?.split(",").map((key) => key.trim()).filter(Boolean),
    group: value("--group"),
    concurrency: Number(value("--concurrency") ?? DEFAULT_CONCURRENCY),
  };
}

function selectAssets(options) {
  return assetList().filter((asset) =>
    (!options.only || options.only.includes(asset.key)) && (!options.group || asset.key.startsWith(`${options.group}:`)));
}

function readJson(path, fallback) {
  return existsSync(path) ? JSON.parse(readFileSync(path, "utf8")) : fallback;
}

/** Serialises writes to the shared state and manifest files from parallel workers. */
class Store {
  constructor() {
    mkdirSync(MODELS, { recursive: true });
    this.tasks = readJson(STATE_FILE, {});
    this.manifest = readJson(MANIFEST, { assets: {} });
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
    if (!field) {
      return this.flush();
    }
    delete tasks[field];
    return this.flush();
  }

  publish(key, entry) {
    this.manifest.assets[key] = entry;
    return this.flush();
  }

  flush() {
    this.writing = this.writing.then(() => Promise.all([
      writeFile(STATE_FILE, JSON.stringify(this.tasks, null, 2) + "\n"),
      writeFile(MANIFEST, JSON.stringify(this.manifest, null, 2) + "\n"),
    ]));
    return this.writing;
  }
}

function pickClips(library, rig) {
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

async function step(store, asset, field, run) {
  const existing = store.tasks[asset.key]?.[field];
  if (existing) {
    return existing;
  }
  const taskId = await run();
  await store.record(asset.key, { [field]: taskId });
  return taskId;
}

async function generate(client, store, library, asset, log) {
  const isRigged = asset.rig !== "none";
  const preview = await step(store, asset, "preview", () => client.create(TEXT_TO_3D, {
    mode: "preview",
    prompt: asset.prompt,
    model_type: "smart-topology",
    ai_model: GEOMETRY_MODEL,
    target_polycount: TARGET_POLYCOUNT,
    ...(asset.rig === "biped" ? { pose_mode: "t-pose" } : {}),
    target_formats: ["glb"],
  }));
  await client.waitFor(TEXT_TO_3D, preview, (progress) => log(`меш ${progress}%`));

  const refine = await step(store, asset, "refine", () => client.create(TEXT_TO_3D, {
    mode: "refine",
    preview_task_id: preview,
    texture_prompt: asset.prompt.slice(0, MAX_TEXTURE_PROMPT),
    target_formats: ["glb"],
  }));
  const textured = await client.waitFor(TEXT_TO_3D, refine, (progress) => log(`текстура ${progress}%`));

  const [kind, id] = asset.key.split(":");
  const modelPath = join(MODELS, kind, `${id}.glb`);
  const entry = { model: relative(PUBLIC, modelPath), rig: asset.rig, height: asset.height, clips: {} };

  if (!isRigged) {
    await client.download(textured.model_urls.glb, modelPath);
    await store.publish(asset.key, entry);
    return;
  }

  const rig = await step(store, asset, "rig", () => client.create(RIGGING, {
    input_task_id: refine,
    animation_type: asset.rig,
    ...(asset.rig === "biped" ? { height_meters: asset.height } : {}),
  }));
  const rigged = await client.waitFor(RIGGING, rig, (progress) => log(`скелет ${progress}%`));
  await client.download(rigged.result.rigged_character_glb_url, modelPath);

  const clips = pickClips(library, asset.rig);
  if (Object.keys(clips).length > 0) {
    const animation = await step(store, asset, "animation", () => client.create(ANIMATIONS, {
      rig_task_id: rig,
      action_ids: Object.values(clips).map((clip) => clip.action_id),
    }));
    const animated = await client.waitFor(ANIMATIONS, animation, (progress) => log(`анимации ${progress}%`));
    const animationPath = join(MODELS, kind, `${id}.anim.glb`);
    await client.download(animated.result.animation_glb_url, animationPath);
    entry.animations = relative(PUBLIC, animationPath);
    entry.clips = Object.fromEntries(Object.entries(clips).map(([slot, clip]) => [slot, clip.name]));
  }
  await store.publish(asset.key, entry);
}

async function runPool(items, concurrency, worker) {
  const queue = [...items];
  await Promise.all(Array.from({ length: Math.max(1, concurrency) }, async () => {
    for (let item = queue.shift(); item; item = queue.shift()) {
      await worker(item);
    }
  }));
}

async function main() {
  const options = parseArgs(process.argv.slice(2));
  const assets = selectAssets(options);
  const store = new Store();
  const pending = assets.filter((asset) => !store.manifest.assets[asset.key]);
  const rigged = pending.filter((asset) => asset.rig !== "none").length;
  console.log(`Ассетов: ${assets.length}, осталось сгенерировать: ${pending.length}`);
  console.log(`Задач Meshy: меш ${pending.length}, текстура ${pending.length}, скелет ${rigged}, анимации ${rigged}`);
  if (options.dryRun) {
    pending.forEach((asset) => console.log(`  ${asset.key} [${asset.rig}, ${asset.height} м]: ${asset.prompt}`));
    return;
  }
  const apiKey = loadApiKey(join(ROOT, ".env.local"));
  if (!apiKey) {
    console.error("Нет MESHY_API_KEY: задайте переменную окружения или строку MESHY_API_KEY=msy_… в client-web/.env.local");
    process.exit(1);
  }
  const client = new MeshyClient(apiKey);
  const library = rigged > 0 ? await client.animationLibrary() : [];
  const failures = [];
  await runPool(pending, options.concurrency, async (asset) => {
    let lastLine = "";
    const log = (text) => {
      if (text !== lastLine) {
        lastLine = text;
        console.log(`${asset.key}: ${text}`);
      }
    };
    try {
      await generate(client, store, library, asset, log);
      console.log(`${asset.key}: готово`);
    } catch (error) {
      failures.push(asset.key);
      console.error(`${asset.key}: ошибка — ${error.message}`);
      await store.forget(asset.key, error.failedTaskId);
    }
  });
  await store.flush();
  console.log(failures.length ? `Не удалось: ${failures.join(", ")} (перезапуск продолжит с места ошибки)` : "Все ассеты готовы.");
}

await main();
