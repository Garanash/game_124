import "./styles.css";
import { ContentCatalog } from "./api/content";
import { CharacterSummary, GatewayClient } from "./api/gateway";
import { GameSession } from "./game/gameSession";
import { showAuthScreen } from "./ui/authScreen";
import { showCharacterList } from "./ui/characterScreen";
import { errorText, h, replaceChildren } from "./ui/dom";

const uiRoot = document.getElementById("ui")!;
const gameRoot = document.getElementById("game")!;
const gateway = new GatewayClient();
let catalog: ContentCatalog | null = null;

async function loadCatalog(): Promise<ContentCatalog> {
  catalog ??= await gateway.catalog();
  return catalog;
}

function showMessage(text: string, retry: () => void): void {
  replaceChildren(uiRoot, h("div", { class: "screen center" },
    h("div", { class: "panel" }, h("p", {}, text), h("button", { class: "primary", onclick: retry }, "Повторить"))));
}

async function showCharacters(notice?: string): Promise<void> {
  if (!gateway.isSignedIn) {
    showAuthScreen(uiRoot, gateway, () => void showCharacters());
    return;
  }
  try {
    const content = await loadCatalog();
    await showCharacterList(uiRoot, gateway, content, { onPlay: (character) => void play(character), onSignOut });
    if (notice) {
      uiRoot.querySelector(".panel")?.prepend(h("p", { class: "form-error" }, notice));
    }
  } catch (error) {
    if (!gateway.isSignedIn) {
      showAuthScreen(uiRoot, gateway, () => void showCharacters());
      return;
    }
    showMessage(`Сервер недоступен: ${errorText(error)}`, () => void showCharacters());
  }
}

function onSignOut(): void {
  gateway.signOut();
  void showCharacters();
}

async function play(character: CharacterSummary): Promise<void> {
  const content = await loadCatalog();
  replaceChildren(uiRoot, h("div", { class: "screen center loading" }, h("p", {}, `${character.name} входит в мир…`)));
  const session = new GameSession(gameRoot, gateway, content, character.characterId, (reason) => void showCharacters(reason));
  try {
    await session.start();
    replaceChildren(uiRoot);
  } catch (error) {
    session.leave(`Не удалось войти в мир: ${errorText(error)}`);
  }
}

void showCharacters();
