type Child = Node | string | null | undefined | false;
type Attributes = Record<string, string | number | boolean | EventListener | undefined>;

/** Tiny hyperscript helper: h("button", { class: "primary", onclick: go }, "Play"). */
export function h<K extends keyof HTMLElementTagNameMap>(tag: K, attributes: Attributes = {}, ...children: Child[]): HTMLElementTagNameMap[K] {
  const element = document.createElement(tag);
  for (const [name, value] of Object.entries(attributes)) {
    if (value === undefined || value === false) {
      continue;
    }
    if (name.startsWith("on") && typeof value === "function") {
      element.addEventListener(name.slice(2), value);
    } else if (value === true) {
      element.setAttribute(name, "");
    } else {
      element.setAttribute(name, String(value));
    }
  }
  for (const child of children) {
    if (child !== null && child !== undefined && child !== false) {
      element.append(child);
    }
  }
  return element;
}

export function replaceChildren(parent: HTMLElement, ...children: Child[]): void {
  parent.replaceChildren(...children.filter((child): child is Node | string => child !== null && child !== undefined && child !== false));
}

export function errorText(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
