// Generates a race appearance kit with Meshy (see scripts/meshy/kits.mjs) and publishes it to manifest.kits.
// Resumable like meshy-generate.mjs: task ids are kept in scripts/meshy/tasks.json.
//
// Usage:
//   node scripts/meshy-kit.mjs --race elf --dry-run              # jobs, prompts and credits, spends nothing
//   node scripts/meshy-kit.mjs --race elf --only kit:elf:hair:0  # selected jobs
//   node scripts/meshy-kit.mjs --race elf                        # the whole kit
//   node scripts/meshy-kit.mjs --race elf --redo kit:elf:hair:1  # discard a rejected part and generate it again
//   node scripts/meshy-kit.mjs --race elf --drop kit:elf:hair:1  # discard a rejected part now; a later run remakes it
import { join } from "node:path";
import { MeshyClient, loadApiKey } from "./meshy/api.mjs";
import { KIT_JOB_CREDITS, KITS, kitJobs } from "./meshy/kits.mjs";
import { modelsPath, publicPath, ROOT, runJobs, Store } from "./meshy/pipeline.mjs";

const DEFAULT_CONCURRENCY = 4;

function parseArgs(argv) {
  const value = (flag) => {
    const index = argv.indexOf(flag);
    return index >= 0 ? argv[index + 1] : undefined;
  };
  return {
    raceId: value("--race") ?? "elf",
    dryRun: argv.includes("--dry-run"),
    only: value("--only")?.split(",").map((key) => key.trim()).filter(Boolean),
    redo: value("--redo")?.split(",").map((key) => key.trim()).filter(Boolean) ?? [],
    drop: value("--drop")?.split(",").map((key) => key.trim()).filter(Boolean) ?? [],
    concurrency: Number(value("--concurrency") ?? DEFAULT_CONCURRENCY),
  };
}

/** Where a job's result lives inside manifest.kits[raceId]. */
function manifestPath(job) {
  switch (job.kind) {
    case "base": return ["bases", job.sex];
    case "skin": return ["skins", job.sex, String(job.index)];
    case "outfit": return ["outfits", job.outfit, job.sex];
    case "hair": return ["hair", String(job.index)];
  }
}

function isPublished(manifestKit, job) {
  return Boolean(manifestPath(job).reduce((node, segment) => node?.[segment], manifestKit));
}

async function runJob(job, item, kit, library) {
  const directory = ["kits", kit.raceId];
  const baseRefine = () => {
    const refine = job.store.tasks[`kit:${kit.raceId}:base:${item.sex}`]?.refine;
    if (!refine) {
      throw new Error(`сначала нужно тело ${item.sex}`);
    }
    return refine;
  };
  switch (item.kind) {
    case "base": {
      const { refine } = await job.textTo3d({ prompt: item.prompt, pose: "t-pose" });
      return job.rigAndAnimate({
        inputTask: refine, rig: "biped", height: kit.heights[item.sex], library,
        modelPath: modelsPath(...directory, `base-${item.sex}.glb`),
        animationPath: modelsPath(...directory, `base-${item.sex}.anim.glb`),
      });
    }
    case "skin":
      return job.retexture({ inputTask: baseRefine(), prompt: item.prompt, texturePath: modelsPath(...directory, `skin-${item.sex}-${item.index}.jpg`) });
    case "outfit":
      return job.retexture({ inputTask: baseRefine(), prompt: item.prompt, texturePath: modelsPath(...directory, `outfit-${item.outfit}-${item.sex}.jpg`) });
    case "hair": {
      const { task } = await job.textTo3d({ prompt: item.prompt });
      const path = modelsPath(...directory, `hair-${item.index}.glb`);
      await job.client.download(task.model_urls.glb, path);
      return publicPath(path);
    }
  }
}

async function main() {
  const options = parseArgs(process.argv.slice(2));
  const kit = KITS[options.raceId];
  if (!kit) {
    console.error(`Нет набора для расы ${options.raceId}. Есть: ${Object.keys(KITS).join(", ")}`);
    process.exit(1);
  }
  const store = new Store();
  const jobs = kitJobs(kit);
  const discarded = [...options.redo, ...options.drop];
  if (!options.dryRun) {
    for (const job of jobs.filter((candidate) => discarded.includes(candidate.key))) {
      delete store.tasks[job.key];
      store.unpublishKit(kit.raceId, manifestPath(job));
    }
    await store.flush();
  }
  const pending = jobs
    .filter((job) => options.redo.includes(job.key) || !isPublished(store.manifest.kits[kit.raceId], job))
    .filter((job) => !options.drop.includes(job.key))
    .filter((job) => !options.only || options.only.includes(job.key) || options.redo.includes(job.key));
  if (options.drop.length > 0 && options.redo.length === 0 && !options.only) {
    console.log(`Убрано из набора: ${options.drop.join(", ")}`);
    return;
  }
  const credits = pending.reduce((sum, job) => sum + KIT_JOB_CREDITS[job.kind], 0);
  console.log(`Набор ${kit.raceId}: заданий ${pending.length}, примерно ${credits} кредитов`);
  if (options.dryRun) {
    pending.forEach((job) => console.log(`  ${job.key}: ${job.prompt}`));
    return;
  }
  const apiKey = loadApiKey(join(ROOT, ".env.local"));
  if (!apiKey) {
    console.error("Нет MESHY_API_KEY: задайте переменную окружения или строку MESHY_API_KEY=msy_… в client-web/.env.local");
    process.exit(1);
  }
  const client = new MeshyClient(apiKey);
  const library = pending.some((job) => job.kind === "base") ? await client.animationLibrary() : [];
  const run = async (job, item) => store.publishKit(kit.raceId, manifestPath(item), await runJob(job, item, kit, library));
  const failures = [
    ...await runJobs(client, store, pending.filter((job) => job.kind === "base"), options.concurrency, run),
    ...await runJobs(client, store, pending.filter((job) => job.kind !== "base"), options.concurrency, run),
  ];
  store.manifest.kits[kit.raceId].heights = kit.heights;
  await store.flush();
  console.log(failures.length ? `Не удалось: ${failures.join(", ")} (перезапуск продолжит с места ошибки)` : "Набор готов.");
}

await main();
