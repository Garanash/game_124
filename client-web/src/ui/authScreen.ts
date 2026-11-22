import { GatewayClient } from "../api/gateway";
import { errorText, h, replaceChildren } from "./dom";

const MIN_PASSWORD_LENGTH = 8;

export function showAuthScreen(root: HTMLElement, gateway: GatewayClient, onSignedIn: () => void): void {
  const email = h("input", { type: "email", placeholder: "email", autocomplete: "email", required: true });
  const password = h("input", { type: "password", placeholder: `пароль (от ${MIN_PASSWORD_LENGTH} символов)`, autocomplete: "current-password", required: true });
  const status = h("p", { class: "form-error" });

  const submit = async (mode: "login" | "register") => {
    status.textContent = "";
    if (password.value.length < MIN_PASSWORD_LENGTH) {
      status.textContent = `Пароль должен быть не короче ${MIN_PASSWORD_LENGTH} символов.`;
      return;
    }
    try {
      await (mode === "login" ? gateway.login(email.value, password.value) : gateway.register(email.value, password.value));
      onSignedIn();
    } catch (error) {
      status.textContent = errorText(error);
    }
  };

  const form = h("form", { class: "panel auth-panel", onsubmit: (event: Event) => { event.preventDefault(); void submit("login"); } },
    h("h1", { class: "logo" }, "Realmforge"),
    h("p", { class: "tagline" }, "Пошаговые приключения в живом мире"),
    email,
    password,
    status,
    h("div", { class: "row" },
      h("button", { type: "submit", class: "primary" }, "Войти"),
      h("button", { type: "button", onclick: () => void submit("register") }, "Создать аккаунт")));
  replaceChildren(root, h("div", { class: "screen center" }, form));
  email.focus();
}
