import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react()],
  base: "./",
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": {
        target: "http://127.0.0.1:5099",
        changeOrigin: true
      },
      "/health": {
        target: "http://127.0.0.1:5099",
        changeOrigin: true
      }
    }
  },
  preview: {
    port: 4173,
    strictPort: true
  }
});
