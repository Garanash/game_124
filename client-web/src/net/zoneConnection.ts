import {
  AnyServerEnvelope, ClientMessages, ClientMessageType, decodeServer, encodeClient, PROTOCOL_VERSION,
  ServerMessages, ServerMessageType,
} from "./protocol";

type Handler<K extends ServerMessageType> = (message: ServerMessages[K]) => void;

export interface ZoneAddress { host: string; webSocketPort: number; ticket: string }

/** One WebSocket session with a zone server; reconnecting to another zone means a new instance. */
export class ZoneConnection {
  private readonly socket: WebSocket;
  private readonly handlers = new Map<ServerMessageType, Set<Handler<never>>>();
  private closeListener: ((reason: string) => void) | null = null;

  constructor(address: ZoneAddress) {
    this.socket = new WebSocket(ZoneConnection.url(address));
    this.socket.addEventListener("open", () =>
      this.send("HelloRequest", { ticket: address.ticket, protocolVersion: PROTOCOL_VERSION }));
    this.socket.addEventListener("message", (event) => this.dispatch(event.data));
    this.socket.addEventListener("close", (event) => this.closeListener?.(event.reason || "Соединение закрыто"));
  }

  static url(address: ZoneAddress): string {
    const scheme = window.location.protocol === "https:" ? "wss" : "ws";
    const host = ZoneConnection.reachableHost(address.host);
    return `${scheme}://${host}:${address.webSocketPort}/ws?format=json`;
  }

  /** Local stacks advertise 127.0.0.1; a phone on the same network must use the page's host instead. */
  private static reachableHost(advertised: string): string {
    const isLoopback = advertised === "127.0.0.1" || advertised === "localhost";
    return isLoopback ? window.location.hostname : advertised;
  }

  on<K extends ServerMessageType>(type: K, handler: Handler<K>): void {
    let set = this.handlers.get(type);
    if (!set) {
      set = new Set();
      this.handlers.set(type, set);
    }
    set.add(handler as Handler<never>);
  }

  onClose(listener: (reason: string) => void): void {
    this.closeListener = listener;
  }

  send<K extends ClientMessageType>(type: K, data: ClientMessages[K]): void {
    if (this.socket.readyState === WebSocket.OPEN) {
      this.socket.send(encodeClient(type, data));
    }
  }

  close(): void {
    this.closeListener = null;
    this.socket.close();
  }

  private dispatch(frame: unknown): void {
    if (typeof frame !== "string") {
      return;
    }
    const envelope: AnyServerEnvelope = decodeServer(frame);
    this.handlers.get(envelope.type)?.forEach((handler) => (handler as Handler<typeof envelope.type>)(envelope.data as never));
  }
}
