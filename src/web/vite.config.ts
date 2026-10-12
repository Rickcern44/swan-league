import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

const apiUrl = process.env.API_HTTP;

export default defineConfig({
  plugins: [react()],
  server: {
    host: "0.0.0.0",
    proxy: apiUrl
      ? {
          "/api": {
            changeOrigin: true,
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
