// Generates weapons, buildings, street props, nature and interface textures with Meshy and lists them in
// public/models/props/index.json. Resumable: task ids are kept in scripts/meshy/prop-tasks.json.
//
// Usage:
//   node scripts/meshy-props.mjs --dry-run                       # assets, prompts and credits, spends nothing
//   node scripts/meshy-props.mjs --only weapon:dagger,prop:well  # selected assets
//   node scripts/meshy-props.mjs --redo nature:oak               # generate a rejected asset again
//   node scripts/meshy-props.mjs                                 # everything missing
import { join } from "node:path";
import { MeshyClient, loadApiKey } from "./meshy/api.mjs";
import { ICON_MODEL } from "./meshy/icons.mjs";
import { modelsPath, publicPath, ROOT, runJobs, Store } from "./meshy/pipeline.mjs";
import { PROP_CREDITS, propList, UI_IMAGE_CREDITS, UI_IMAGES } from "./meshy/props.mjs";

const DEFAULT_CONCURRENCY = 6;
const UI_IMAGE_PIXELS = 1024;

function parseArgs(argv) {
  const list = (flag) => {
    const index = argv.indexOf(flag);
    return index >= 0 ? argv[index + 1].split(",").map((key) => key.trim()).filter(Boolean) : undefined;
  };
  return { dryRun: argv.includes("--dry-run"), only: list("--only"), redo: list("--redo") ?? [] };
}

function allJobs() {
  const models = propList().map((asset) => ({ ...asset, kind: "model" }));
  const images = Object.entries(UI_IMAGES).map(([key, prompt]) => ({ key, prompt, kind: "image" }));
  return [...models, ...images];
}

function isPublished(store, job) {
  return job.kind === "model" ? Boolean(store.manifest.props[job.key]) : Boolean(store.manifest.ui[job.key]);
}

async function generateModel(job, asset) {
  const { task } = await job.textTo3d({ prompt: asset.prompt, polycount: asset.polycount });
  const [group, id] = asset.key.split(":");
  const path = modelsPath("props", group, `${id}.glb`);
  await job.client.download(task.model_urls.glb, path);
  job.store.manifest.props[asset.key] = { model: publicPath(path), height: asset.height };
}

async function generateImage(job, image) {
  job.store.manifest.ui[image.key] = await job.textToImage({
    prompt: image.prompt, model: ICON_MODEL, imagePath: modelsPath("props", "ui", `${image.key.split(":")[1]}.jpg`), sizePixels: UI_IMAGE_PIXELS,
  });
}

async function main() {
  const options = parseArgs(process.argv.slice(2));
  const store = new Store({
    stateFile: join(ROOT, "scripts", "meshy", "prop-tasks.json"),
    manifestFile: modelsPath("props", "index.json"),
    emptyManifest: { props: {}, ui: {} },
  });
  for (const key of options.redo) {
    delete store.tasks[key];
    delete store.manifest.props[key];
    delete store.manifest.ui[key];
  }
  const pending = allJobs()
    .filter((job) => !isPublished(store, job))
    .filter((job) => !options.only || options.only.includes(job.key) || options.redo.includes(job.key));
  const credits = pending.reduce((sum, job) => sum + (job.kind === "model" ? PROP_CREDITS : UI_IMAGE_CREDITS), 0);
  console.log(`Ассетов: ${pending.length}, примерно ${credits} кредитов`);
  if (options.dryRun) {
    pending.forEach((job) => console.log(`  ${job.key}${job.height ? ` [${job.height} м, ${job.polycount} полигонов]` : ""}: ${job.prompt}`));
    return;
  }
  const apiKey = loadApiKey(join(ROOT, ".env.local"));
  if (!apiKey) {
    console.error("Нет MESHY_API_KEY: задайте переменную окружения или строку MESHY_API_KEY=msy_… в client-web/.env.local");
    process.exit(1);
  }
  const client = new MeshyClient(apiKey);
  const failures = await runJobs(client, store, pending, DEFAULT_CONCURRENCY, async (job, asset) => {
    await (asset.kind === "model" ? generateModel(job, asset) : generateImage(job, asset));
    await store.flush();
  });
  console.log(failures.length ? `Не удалось: ${failures.join(", ")} (перезапуск продолжит с места ошибки)` : "Ассеты готовы.");
}

await main();
