// Smoke test for the JSON zone protocol: registers a fresh hero, attacks the nearest mob of the start zone
// and keeps attacking until the encounter ends, then prints the XP stored by the gateway.
// Usage: node scripts/combat-bot.mjs [raceId] [mobIdFragment]
const GATEWAY = process.env.REALMFORGE_GATEWAY ?? "http://127.0.0.1:5080";
const RACE_ID = process.argv[2] ?? "elf";
const MOB_FRAGMENT = process.argv[3] ?? "dummy";
const ATTACK = 1;
const MOVE = 0;
const END_TURN = 4;
const TIMEOUT_MS = 90_000;
const STANDARD_ARRAY = [15, 14, 13, 12, 10, 8];

async function api(method, path, token, body) {
  const response = await fetch(GATEWAY + path, {
    method,
    headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  if (!response.ok) throw new Error(`${method} ${path} → ${response.status} ${text}`);
  return text ? JSON.parse(text) : null;
}

function botName() {
  const letters = "абвгдежзиклмнопрстуфх";
  return "Бот" + [...Date.now().toString().slice(-5)].map((digit) => letters[Number(digit)]).join("");
}

const email = `bot${Date.now()}@realmforge.test`;
const { token } = await api("POST", "/auth/register", null, { email, password: "password123" });
const hero = await api("POST", "/characters", token, { name: botName(), raceId: RACE_ID, alignment: "TrueNeutral", abilities: STANDARD_ARRAY });
const ticket = await api("POST", `/characters/${hero.characterId}/enter`, token, {});
console.log("zone", ticket.zoneId);

const socket = new WebSocket(`ws://127.0.0.1:${ticket.webSocketPort}/ws?format=json`);
const send = (type, data) => socket.send(JSON.stringify({ type, data }));
const entities = new Map();
let selfId = 0;
let encounter = null;
let finished = false;

socket.onopen = () => send("HelloRequest", { ticket: ticket.ticket, protocolVersion: 1 });
socket.onmessage = (event) => {
  const { type, data } = JSON.parse(event.data);
  switch (type) {
    case "WelcomeMessage":
      selfId = data.entityId;
      break;
    case "EntitySpawnMessage":
      entities.set(data.entityId, data);
      break;
    case "EncounterStartedMessage":
      encounter = data;
      console.log("encounter started", data.combatants.length, "combatants");
      break;
    case "CombatEventsMessage":
      trackEvents(data.events);
      for (const combatEvent of data.events.filter((entry) => ![1, 2, 14].includes(entry.type))) console.log("  event", JSON.stringify(combatEvent));
      if (data.currentTurnEntityId === selfId && encounter) actOnTurn();
      break;
    case "EncounterEndedMessage":
      console.log("ended", JSON.stringify(data));
      finished = true;
      setTimeout(report, 1500);
      break;
    case "ErrorMessage":
      console.log("error", data.code, data.text);
      break;
    case "InventoryChangedMessage":
    case "QuestUpdateMessage":
      console.log(type, JSON.stringify(data));
      break;
  }
};

let engaged = false;
const engageTimer = setInterval(() => {
  if (engaged || !selfId) return;
  const target = [...entities.values()].find((entity) => entity.kind === 2 && entity.definitionId.includes(MOB_FRAGMENT));
  if (!target) return;
  engaged = true;
  console.log("engaging", target.name, target.entityId);
  send("InteractRequest", { targetEntityId: target.entityId, optionId: "" });
}, 500);

const MOVED = 3;
const DIED = 13;
const SPEED_CELLS = 6;
const dead = new Set();

function trackEvents(events) {
  for (const combatEvent of events) {
    const combatant = encounter?.combatants.find((entry) => entry.entityId === combatEvent.actorId);
    if (combatEvent.type === MOVED && combatant) {
      combatant.cellX = combatEvent.cellX;
      combatant.cellY = combatEvent.cellY;
    }
    if (combatEvent.type === DIED) dead.add(combatEvent.targetId || combatEvent.actorId);
  }
}

const command = (commandType, extra = {}) =>
  send("CombatCommandRequest", { commandType, cellX: 0, cellY: 0, targetId: 0, abilityId: "", ...extra });

let lastTurnAction = 0;
function actOnTurn() {
  const now = Date.now();
  if (now - lastTurnAction < 300) return;
  lastTurnAction = now;
  const self = encounter.combatants.find((combatant) => combatant.entityId === selfId);
  const enemy = encounter.combatants
    .filter((combatant) => combatant.team !== self.team && !dead.has(combatant.entityId))
    .sort((a, b) => chebyshev(self, a) - chebyshev(self, b))[0];
  if (!enemy) return;
  if (chebyshev(self, enemy) > 1) {
    const step = (from, to) => from + Math.sign(to - from) * Math.min(SPEED_CELLS, Math.max(0, Math.abs(to - from) - 1));
    command(MOVE, { cellX: step(self.cellX, enemy.cellX), cellY: step(self.cellY, enemy.cellY) });
  }
  setTimeout(() => command(ATTACK, { targetId: enemy.entityId }), 300);
  setTimeout(() => command(END_TURN), 700);
}

function chebyshev(a, b) {
  return Math.max(Math.abs(a.cellX - b.cellX), Math.abs(a.cellY - b.cellY));
}

async function report() {
  const state = await api("GET", `/characters/${hero.characterId}`, token);
  console.log("stored xp", state.sheet.xp, "level", state.sheet.level, "gold", state.sheet.gold);
  clearInterval(engageTimer);
  socket.close();
  process.exit(0);
}

setTimeout(() => {
  console.log(finished ? "done" : "timeout");
  void report();
}, TIMEOUT_MS);
