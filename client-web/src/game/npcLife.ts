import { NpcDefinition, ScheduleEntry } from "../api/content";
import { Activity } from "../render/characters/rig";

/** Mirrors Realmforge.Rules.World.GameClock: one in-game day lasts two real hours. */
export const REAL_SECONDS_PER_GAME_DAY = 7200;
const HOURS_PER_DAY = 24;
const AT_POST_METERS = 4;

export function hourOfDay(unixSeconds: number): number {
  const dayFraction = (((unixSeconds % REAL_SECONDS_PER_GAME_DAY) + REAL_SECONDS_PER_GAME_DAY) % REAL_SECONDS_PER_GAME_DAY) / REAL_SECONDS_PER_GAME_DAY;
  return dayFraction * HOURS_PER_DAY;
}

/** Same rule as NpcSchedule.ActiveEntry, including ranges that wrap past midnight. */
export function activeEntry(npc: NpcDefinition, hour: number): ScheduleEntry | undefined {
  const covers = (entry: ScheduleEntry) => entry.fromHour <= entry.toHour
    ? hour >= entry.fromHour && hour < entry.toHour
    : hour >= entry.fromHour || hour < entry.toHour;
  return npc.schedule.find(covers) ?? npc.schedule[0];
}

const ACTIVITY_ANIMATIONS: Record<string, Activity> = {
  teach: "teach",
  council: "talk",
  tavern: "drink",
  trade: "trade",
  home: "sit",
  sleep: "sleep",
  patrol_gate: "lookout",
  patrol_square: "lookout",
  auction: "gavel",
  workshop: "hammer",
};

const ACTIVITY_LABELS: Record<string, string> = {
  teach: "обучает",
  council: "на совете",
  tavern: "отдыхает в таверне",
  trade: "торгует",
  home: "дома",
  sleep: "спит",
  patrol_gate: "в дозоре у ворот",
  patrol_square: "патрулирует площадь",
  auction: "ведёт торги",
  workshop: "работает в мастерской",
};

export interface NpcActivity {
  animation: Activity;
  label: string;
}

/** On the way to the next schedule spot the NPC just walks; the activity starts once it arrives. */
export function npcActivity(npc: NpcDefinition, unixSeconds: number, position: { x: number; z: number }): NpcActivity {
  const entry = activeEntry(npc, hourOfDay(unixSeconds));
  if (!entry) {
    return { animation: "idle", label: "" };
  }
  const atPost = Math.hypot(entry.position.x - position.x, entry.position.z - position.z) <= AT_POST_METERS;
  const patrolling = entry.activity.startsWith("patrol");
  if (!atPost && !patrolling) {
    return { animation: "idle", label: "идёт по делам" };
  }
  return { animation: ACTIVITY_ANIMATIONS[entry.activity] ?? "idle", label: ACTIVITY_LABELS[entry.activity] ?? "" };
}

/** 0 at midnight, 1 at noon; used for lighting and lit windows. */
export function daylight(hour: number): number {
  const sun = Math.sin(((hour - 6) / 12) * Math.PI);
  return Math.max(0, Math.min(1, sun * 1.4 + 0.15));
}
