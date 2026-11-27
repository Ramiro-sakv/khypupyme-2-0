// Configura Vite para servir y compilar React. El entorno de pruebas usa otros puertos para trabajar en paralelo con el local.

import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig(({ mode }) => {
  const testing = mode === "test";
  // La API de desarrollo está en 5182 y la de pruebas en 5183. El proxy de Vite permite que React use rutas relativas.
  const api = "http://127.0.0.1:" + (testing ? "5183" : "5182");
  // Comparte la conexión entre el servidor de desarrollo y la vista previa local. strictPort evita arrancar en un puerto distinto sin avisar.
  const localServer = {
    host: "127.0.0.1",
    port: testing ? 5283 : 5282,
    strictPort: true,
    proxy: { "/api": api, "/connect": api, "/signin-google": api },
  };

  return { plugins: [react()], server: localServer, preview: localServer };
});
