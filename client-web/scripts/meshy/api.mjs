// Thin client for the Meshy REST API (the same API the Meshy MCP server wraps).
import { readFileSync, existsSync } from "node:fs";
import { writeFile, mkdir } from "node:fs/promises";
import { dirname } from "node:path";

const API_HOST = process.env.MESHY_API_HOST ?? "https://api.meshy.ai";
const POLL_INTERVAL_MS = 8000;
const RATE_LIMIT_BACKOFF_MS = 15000;
const MAX_RETRIES = 5;
const TERMINAL_FAILURES = new Set(["FAILED", "CANCELED", "EXPIRED"]);

/** Reads MESHY_API_KEY from the environment or client-web/.env.local, never from tracked files. */
export function loadApiKey(envFile) {
  if (process.env.MESHY_API_KEY) {
    return process.env.MESHY_API_KEY;
  }
  if (existsSync(envFile)) {
    const line = readFileSync(envFile, "utf8").split("\n").find((entry) => entry.startsWith("MESHY_API_KEY="));
    if (line) {
      return line.slice("MESHY_API_KEY=".length).trim();
    }
  }
  return null;
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

export class MeshyClient {
  constructor(apiKey) {
    this.apiKey = apiKey;
  }

  async request(method, path, body) {
    for (let attempt = 0; ; attempt++) {
      const response = await fetch(API_HOST + path, {
        method,
        headers: { Authorization: `Bearer ${this.apiKey}`, "Content-Type": "application/json" },
        body: body === undefined ? undefined : JSON.stringify(body),
      });
      if (response.status === 429 && attempt < MAX_RETRIES) {
        await sleep(RATE_LIMIT_BACKOFF_MS * (attempt + 1));
        continue;
      }
      const text = await response.text();
      if (!response.ok) {
        throw new Error(`Meshy ${method} ${path} → ${response.status}: ${text.slice(0, 400)}`);
      }
      return text ? JSON.parse(text) : null;
    }
  }

  /** POST returns { result: taskId }. */
  async create(path, body) {
    return (await this.request("POST", path, body)).result;
  }

  async waitFor(path, taskId, onProgress) {
    for (;;) {
      const task = await this.request("GET", `${path}/${taskId}`);
      if (task.status === "SUCCEEDED") {
        return task;
      }
      if (TERMINAL_FAILURES.has(task.status)) {
        throw Object.assign(new Error(`Meshy task ${taskId} ${task.status}: ${JSON.stringify(task.task_error)}`), { failedTaskId: taskId });
      }
      onProgress?.(task.progress ?? 0);
      await sleep(POLL_INTERVAL_MS);
    }
  }

  animationLibrary() {
    return this.request("GET", "/openapi/v1/animations/library");
  }

  async download(url, destination) {
    const response = await fetch(url);
    if (!response.ok) {
      throw new Error(`Download ${destination} → ${response.status}`);
    }
    await mkdir(dirname(destination), { recursive: true });
    await writeFile(destination, Buffer.from(await response.arrayBuffer()));
  }
}
