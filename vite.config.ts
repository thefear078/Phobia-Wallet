// @lovable.dev/vite-tanstack-config already includes the following — do NOT add them manually
// or the app will break with duplicate plugins:
//   - tanstackStart, viteReact, tailwindcss, tsConfigPaths, nitro (build-only using cloudflare as a default target),
//     componentTagger (dev-only), VITE_* env injection, @ path alias, React/TanStack dedupe,
//     error logger plugins, and sandbox detection (port/host/strictPort).
// You can pass additional config via defineConfig({ vite: { ... }, etc... }) if needed.
import { defineConfig } from "@lovable.dev/vite-tanstack-config";

export default defineConfig({
  tanstackStart: {
    server: { entry: "server" },
  },
  vite: {
    server: {
      proxy: {
        "/auth": { target: "http://localhost:3001", changeOrigin: true },
        "/users": { target: "http://localhost:3001", changeOrigin: true },
        "/wallets": { target: "http://localhost:3001", changeOrigin: true },
        "/bank-accounts": { target: "http://localhost:3001", changeOrigin: true },
        // ^/p2p/ (not bare /p2p) — the /p2p page itself is rendered by the app
        "^/p2p/": { target: "http://localhost:3001", changeOrigin: true },
        "/rates": { target: "http://localhost:3001", changeOrigin: true },
        "/kyc": { target: "http://localhost:3001", changeOrigin: true },
        "/webhooks": { target: "http://localhost:3001", changeOrigin: true },
        "/telegram": { target: "http://localhost:3001", changeOrigin: true },
        "/health": { target: "http://localhost:3001", changeOrigin: true },
      },
    },
  },
});
