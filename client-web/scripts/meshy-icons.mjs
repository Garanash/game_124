// Paints inventory icons for every item in content/items with Meshy text-to-image and lists them in
// public/models/icons/index.json. Resumable: task ids are kept in scripts/meshy/icon-tasks.json.
//
// Usage:
//   node scripts/meshy-icons.mjs --dry-run            # items, prompts and credits, spends nothing
//   node scripts/meshy-icons.mjs --only dagger,herb   # selected items
//   node scripts/meshy-icons.mjs --redo dagger        # paint a rejected icon again
//   node scripts/meshy-icons.mjs                      # every item without an icon
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { MeshyClient, loadApiKey } from "./meshy/api.mjs";
import { ICON_CREDITS, ICON_MODEL, iconPrompt } from "./meshy/icons.mjs";
import { modelsPath, ROOT, runJobs, Store } from "./meshy/pipeline.mjs";

const CONTENT_ITEMS = join(ROOT, "..", "content", "items");
const ICON_PIXELS = 256;
const DEFAULT_CONCURRENCY = 6;
const ITEM_ID_PATTERN = /^- (?:\{ )?id: ([a-z0-9_]+)/gm;

function parseArgs(argv) {
  const list = (flag) => {
    const index = argv.indexOf(flag);
    return index >= 0 ? argv[index + 1].split(",").map((id) => id.trim()).filter(Boolean) : undefined;
  };
  return { dryRun: argv.includes("--dry-run"), only: list("--only"), redo: list("--redo") ?? [] };
}

function contentItemIds() {
  return readdirSync(CONTENT_ITEMS)
    .filter((file) => file.endsWith(".yaml"))
    .flatMap((file) => [...readFileSync(join(CONTENT_ITEMS, file), "utf8").matchAll(ITEM_ID_PATTERN)].map((match) => match[1]));
}

async function main() {
  const options = parseArgs(process.argv.slice(2));
  const store = new Store({
    stateFile: join(ROOT, "scripts", "meshy", "icon-tasks.json"),
    manifestFile: modelsPath("icons", "index.json"),
    emptyManifest: { icons: {} },
  });
  const ids = contentItemIds();
  ids.filter((id) => !iconPrompt(id)).forEach((id) => console.warn(`Нет описания иконки для ${id} (scripts/meshy/icons.mjs)`));
  for (const id of options.redo) {
    delete store.tasks[`icon:${id}`];
    delete store.manifest.icons[id];
  }
  const pending = ids
    .filter((id) => iconPrompt(id) && !store.manifest.icons[id])
    .filter((id) => !options.only || options.only.includes(id) || options.redo.includes(id))
    .map((id) => ({ key: `icon:${id}`, id, prompt: iconPrompt(id) }));
  console.log(`Иконки: ${pending.length}, примерно ${pending.length * ICON_CREDITS} кредитов (${ICON_MODEL})`);
  if (options.dryRun) {
    pending.forEach((item) => console.log(`  ${item.id}: ${item.prompt}`));
    return;
  }
  const apiKey = loadApiKey(join(ROOT, ".env.local"));
  if (!apiKey) {
    console.error("Нет MESHY_API_KEY: задайте переменную окружения или строку MESHY_API_KEY=msy_… в client-web/.env.local");
    process.exit(1);
  }
  const client = new MeshyClient(apiKey);
  const failures = await runJobs(client, store, pending, DEFAULT_CONCURRENCY, async (job, item) => {
    store.manifest.icons[item.id] = await job.textToImage({
      prompt: item.prompt, model: ICON_MODEL, imagePath: modelsPath("icons", `${item.id}.jpg`), sizePixels: ICON_PIXELS,
    });
    await store.flush();
  });
  console.log(failures.length ? `Не удалось: ${failures.join(", ")} (перезапуск продолжит с места ошибки)` : "Иконки готовы.");
}

await main();
