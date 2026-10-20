import path from "node:path";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";
import { VitePWA } from "vite-plugin-pwa";

const apiUrl = process.env.API_HTTP;

export default defineConfig({
  plugins: [
    tailwindcss(),
    react(),
    VitePWA({
      injectRegister: false,
      manifest: false,
      registerType: "autoUpdate",
    }),
  ],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  server: {
    host: "0.0.0.0",
    proxy: apiUrl
      ? {
          "/api": {
            changeOrigin: false,
            target: apiUrl,
          },
        }
      : undefined,
  },
  test: {
    environment: "jsdom",
    include: ["src/**/*.test.ts?(x)"],
    setupFiles: "./src/test/setup.ts",
  },
});
