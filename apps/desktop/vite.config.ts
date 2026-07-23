import { fileURLToPath, URL } from "node:url";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig({
  plugins: [react(), tailwindcss()],

  resolve: {
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },

  // Tauri serves the dev server on a fixed port and expects a failure (not a silent
  // port shuffle) if it is taken - otherwise the shell would load the wrong app.
  server: {
    port: 5183,
    strictPort: true,
  },

  build: {
    outDir: "dist",
    emptyOutDir: true,
    target: "chrome110",
    sourcemap: true,
  },

  // Silences Vite's "browser" resolution for the Tauri APIs during SSR-less builds.
  clearScreen: false,
});
