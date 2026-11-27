// Punto de entrada del frontend. Vite carga este archivo desde index.html y React monta App en el elemento root de esa página.

import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";

// Carga primero los estilos de base y después los de cada área para respetar la prioridad de la cascada.
import "./styles.css";
import "./ui/ui.css";
import "./accounts.css";
import "./workspace.css";
import "./businesses/businesses.css";
import "./commerce/commerce.css";
import "./commerce/operations.css";
import "./billing/plan.css";

// Este contenedor conecta el HTML inicial con React. A partir de aquí, los componentes deciden qué se dibuja dentro de la página.
const root = document.getElementById("root");
if (!root) throw new Error("No se encontró el contenedor de KhypuPyme.");

createRoot(root).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
