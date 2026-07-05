import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const port = Number.parseInt(process.env.PORT ?? "5175", 10);

export default defineConfig({
  plugins: [react()],
  server: {
    host: "127.0.0.1",
    port,
    strictPort: true
  }
});
