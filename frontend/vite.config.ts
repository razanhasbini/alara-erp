import path from "node:path"
import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vitest/config"

// The browser talks only to the Vite origin; /api is proxied to ASP.NET, so the session
// and CSRF cookies are same-origin (SameSite=Strict) and no CORS configuration is needed.
const apiTarget = process.env.ALARA_API_URL ?? "http://localhost:5190"

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { "@": path.resolve(import.meta.dirname, "./src") },
  },
  test: { environment: "node", include: ["src/**/*.test.ts"] },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": { target: apiTarget, changeOrigin: false },
    },
  },
})
