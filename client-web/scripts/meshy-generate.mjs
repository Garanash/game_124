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
// Races with an appearance kit (scripts/meshy-kit.mjs) are skipped: the kit replaces their single model.
import { join } from "node:path";
import { MeshyClient, loadApiKey } from "./meshy/api.mjs";
import { assetList } from "./meshy/assets.mjs";
import { KITS } from "./meshy/kits.mjs";
import { modelsPath, ROOT, runJobs, Store } from "./meshy/pipeline.mjs";

const DEFAULT_CONCURRENCY = 2;

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
    !KITS[asset.key.replace(/^race:/, "")]
    && (!options.only || options.only.includes(asset.key))
    && (!options.group || asset.key.startsWith(`${options.group}:`)));
}

async function generate(job, asset, library) {
  const { refine, task } = await job.textTo3d({ prompt: asset.prompt, pose: asset.rig === "biped" ? "t-pose" : undefined });
  const [kind, id] = asset.key.split(":");
  const modelPath = modelsPath(kind, `${id}.glb`);
  if (asset.rig === "none") {
    await job.client.download(task.model_urls.glb, modelPath);
    await job.store.publish(asset.key, { model: `models/${kind}/${id}.glb`, rig: "none", height: asset.height, clips: {} });
    return;
  }
  const entry = await job.rigAndAnimate({
    inputTask: refine, rig: asset.rig, height: asset.height, library,
    modelPath, animationPath: modelsPath(kind, `${id}.anim.glb`),
  });
  await job.store.publish(asset.key, entry);
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
  const failures = await runJobs(client, store, pending, options.concurrency, (job, asset) => generate(job, asset, library));
  console.log(failures.length ? `Не удалось: ${failures.join(", ")} (перезапуск продолжит с места ошибки)` : "Все ассеты готовы.");
}

await main();
