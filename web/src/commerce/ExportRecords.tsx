// Solicita al servidor la exportación comercial del negocio abierto. Guarda el resultado como archivo para consulta o respaldo de registros.

import { useState } from "react";
import { get } from "../api/client";
import { downloadFile } from "../native/platform";
import { Alert, Button } from "../ui/Primitives";
import { useCommerce } from "./CommerceContext";

export function ExportRecords() {
  const { path } = useCommerce();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  // Solicita primero los datos del negocio y solo entonces ofrece el archivo mediante el mecanismo de descarga de la plataforma.
  async function save() {
    setBusy(true);
    setError("");
    try {
      const data = await get<{ business: { id: string } }>(path + "/export");
      await downloadFile(new Blob([JSON.stringify(data, null, 2)], { type: "application/json" }), "khypupyme-" + data.business.id + ".json");
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "No se pudieron exportar los registros.");
    } finally { setBusy(false); }
  }
  return <div><Button variant="secondary" icon="document" disabled={busy} onClick={() => void save()}>
    {busy ? "Preparando exportación…" : "Descargar mis datos"}</Button>{error && <Alert tone="error">{error}</Alert>}</div>;
}
