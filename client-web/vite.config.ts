import { defineConfig } from "vite";

const GATEWAY_URL = process.env.REALMFORGE_GATEWAY ?? "http://127.0.0.1:5080";

// The gateway is reached through /api so the browser never needs CORS; zone servers are
// addressed directly over WebSocket using the host and port the gateway hands out.
export default defineConfig({
  server: {
    port: 5173,
    host: true,
    proxy: {
      "/api": {
        target: GATEWAY_URL,
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ""),
      },
    },
  },
  preview: {
    port: 4173,
    host: true,
    proxy: {
      "/api": {
        target: GATEWAY_URL,
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ""),
      },
    },
  },
});
