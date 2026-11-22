// Race portraits are authored once for the Unity client; the web client serves the same files.
import { cpSync, existsSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const webRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const source = join(webRoot, "..", "client", "Assets", "Game", "Resources", "Portraits");
const target = join(webRoot, "public", "portraits");

if (!existsSync(source)) {
  console.warn(`Portraits not found at ${source}; race cards will show initials.`);
  process.exit(0);
}
mkdirSync(target, { recursive: true });
cpSync(source, target, { recursive: true, filter: (path) => !path.endsWith(".meta") });
console.log(`Portraits synced to ${target}`);
