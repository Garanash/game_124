import * as THREE from "three";
import { createD20, D20, DieStyle } from "../render/d20";
import { h } from "./dom";

const VIEW_PIXELS = 240;
const TUMBLE_SECONDS = 0.75;
const SETTLE_SECONDS = 0.25;
const HOLD_SECONDS = 0.75;
const FADE_SECONDS = 0.2;
const DROP_HEIGHT = 2.6;
const PRESENTATION_TILT = -0.32;
const NATURAL_CRIT = 20;
const NATURAL_FUMBLE = 1;

export type RollOutcome = "hit" | "miss" | "success" | "fail";

export interface DiceRoll {
  natural: number;
  total: number;
  title: string;
  outcome: RollOutcome;
  /** Rolls made by the player's side use the gold die, enemies the dark one. */
  friendly: boolean;
}

const FRIENDLY_STYLE: DieStyle = { body: "#8e1f2a", edge: "#f0c860", ink: "#fbe7a8" };
const HOSTILE_STYLE: DieStyle = { body: "#1e2436", edge: "#9aa8c8", ink: "#e8eeff" };

interface ActiveRoll {
  roll: DiceRoll;
  die: D20;
  elapsed: number;
  spinAxis: THREE.Vector3;
  spinSpeed: number;
  target: THREE.Quaternion;
  settleFrom: THREE.Quaternion | null;
  resolve: () => void;
  revealed: boolean;
  speed: number;
}

/** Centre-screen d20: tumbles, lands on the server's natural roll and explains the result. */
export class DiceRoller {
  readonly root = h("div", { class: "dice-roller hidden" });
  private readonly caption = h("div", { class: "dice-caption" });
  private readonly renderer = new THREE.WebGLRenderer({ alpha: true, antialias: true });
  private readonly scene = new THREE.Scene();
  private readonly camera = new THREE.PerspectiveCamera(35, 1, 0.1, 50);
  private readonly dice = { friendly: createD20(FRIENDLY_STYLE), hostile: createD20(HOSTILE_STYLE) };
  private active: ActiveRoll | null = null;

  constructor() {
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.setSize(VIEW_PIXELS, VIEW_PIXELS);
    this.camera.position.set(0, 0, 7);
    this.scene.add(new THREE.AmbientLight(0xffffff, 1.2));
    const key = new THREE.DirectionalLight(0xfff0d8, 2.4);
    key.position.set(2, 3, 5);
    this.scene.add(key);
    this.root.append(this.renderer.domElement, this.caption);
  }

  get busy(): boolean {
    return this.active !== null;
  }

  /** Resolves once the die has been shown; speed > 1 shortens the whole presentation. */
  roll(roll: DiceRoll, speed = 1): Promise<void> {
    this.finish();
    const die = roll.friendly ? this.dice.friendly : this.dice.hostile;
    this.scene.remove(this.dice.friendly.mesh, this.dice.hostile.mesh);
    this.scene.add(die.mesh);
    const tilt = new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(1, 0, 0), PRESENTATION_TILT);
    return new Promise((resolve) => {
      this.active = {
        roll, die, elapsed: 0, resolve, revealed: false, speed,
        spinAxis: new THREE.Vector3(Math.random() - 0.5, Math.random() - 0.5, Math.random() - 0.5).normalize(),
        spinSpeed: 18 + Math.random() * 8,
        target: tilt.multiply(die.restingOrientation(roll.natural)),
        settleFrom: null,
      };
      die.mesh.quaternion.setFromEuler(new THREE.Euler(Math.random() * 6, Math.random() * 6, 0));
      this.caption.replaceChildren();
      this.caption.className = "dice-caption";
      this.root.classList.remove("hidden", "fade", "crit", "fumble");
    });
  }

  update(deltaSeconds: number): void {
    const active = this.active;
    if (!active) {
      return;
    }
    active.elapsed += deltaSeconds * active.speed;
    const { mesh } = active.die;
    const t = active.elapsed;
    if (t < TUMBLE_SECONDS) {
      const progress = t / TUMBLE_SECONDS;
      const spin = active.spinSpeed * (1 - progress) * deltaSeconds * active.speed;
      mesh.quaternion.premultiply(new THREE.Quaternion().setFromAxisAngle(active.spinAxis, spin));
      mesh.position.y = DROP_HEIGHT * bounce(progress);
    } else if (t < TUMBLE_SECONDS + SETTLE_SECONDS) {
      active.settleFrom ??= mesh.quaternion.clone();
      const progress = (t - TUMBLE_SECONDS) / SETTLE_SECONDS;
      mesh.quaternion.slerpQuaternions(active.settleFrom, active.target, easeOut(progress));
      mesh.position.y = 0;
    } else {
      mesh.quaternion.copy(active.target);
      if (!active.revealed) {
        active.revealed = true;
        this.reveal(active.roll);
      }
      const pulse = 1 + Math.max(0, 0.18 - (t - TUMBLE_SECONDS - SETTLE_SECONDS) * 0.6);
      mesh.scale.setScalar(pulse);
    }
    if (t > TUMBLE_SECONDS + SETTLE_SECONDS + HOLD_SECONDS) {
      this.root.classList.add("fade");
    }
    this.renderer.render(this.scene, this.camera);
    if (t > TUMBLE_SECONDS + SETTLE_SECONDS + HOLD_SECONDS + FADE_SECONDS) {
      this.finish();
    }
  }

  private reveal(roll: DiceRoll): void {
    const modifier = roll.total - roll.natural;
    const math = modifier === 0 ? `${roll.natural}` : `${roll.natural} ${modifier > 0 ? "+" : "−"} ${Math.abs(modifier)} = ${roll.total}`;
    const verdict = verdictText(roll);
    this.caption.className = `dice-caption ${verdict.kind}`;
    this.caption.replaceChildren(h("small", {}, roll.title), h("strong", {}, math), h("em", {}, verdict.text));
    this.root.classList.toggle("crit", roll.natural === NATURAL_CRIT);
    this.root.classList.toggle("fumble", roll.natural === NATURAL_FUMBLE);
  }

  private finish(): void {
    const active = this.active;
    if (!active) {
      return;
    }
    this.active = null;
    active.die.mesh.scale.setScalar(1);
    this.root.classList.add("hidden");
    active.resolve();
  }
}

function verdictText(roll: DiceRoll): { text: string; kind: string } {
  if (roll.natural === NATURAL_CRIT && roll.outcome === "hit") {
    return { text: "Критическое попадание!", kind: "crit" };
  }
  if (roll.natural === NATURAL_FUMBLE && roll.outcome === "miss") {
    return { text: "Провал!", kind: "fumble" };
  }
  switch (roll.outcome) {
    case "hit":
      return { text: "Попадание", kind: "good" };
    case "miss":
      return { text: "Промах", kind: "bad" };
    case "success":
      return { text: "Спасбросок успешен", kind: "good" };
    case "fail":
      return { text: "Спасбросок провален", kind: "bad" };
  }
}

/** Falls from the drop height and bounces twice before resting at 0. */
function bounce(progress: number): number {
  const fall = 1 - progress;
  return Math.abs(Math.cos(progress * Math.PI * 2.5)) * fall * fall;
}

function easeOut(progress: number): number {
  return 1 - Math.pow(1 - progress, 3);
}
