// Consulta disponibilidad para lo que el usuario prepara en el carrito. La confirmación de la operación realiza otra validación en el servidor.

import { useEffect, useState } from "react";
import { change } from "../api/client";
import type { components } from "../api/schema";
import { useCommerce } from "./CommerceContext";
import { fromBase, quantityLabel, unitLabels } from "../retail/measures";
type Schema = components["schemas"];
export type StockLine = Schema["StockLine"];
type StockCheck = Schema["StockCheck"];

// Relaciona la respuesta con una versión y una lista concreta del carrito. Cambiar cantidades o medidas exige consultar la nueva disponibilidad.
export function useStockCheck(lines: StockLine[], receiving: boolean, quote: boolean, version: number) {
  // Usa la ruta del negocio actual y su revisión de datos.
  const { path, revision } = useCommerce();
  const [attempt, setAttempt] = useState(0);
  const [result, setResult] = useState<{ key: string; checks?: StockCheck[]; error?: string }>();
  // Sin líneas, no hay nada que consultar.
  const payload = lines.length ? JSON.stringify({ lines, receiving, quote }) : "";
  // La clave cambia si se edita el carrito, se actualiza el negocio o se pide un reintento.
  const key = JSON.stringify([path, revision, attempt, version, payload]);

  useEffect(() => {
    if (!payload) return;
    const abort = new AbortController();
    // Espera brevemente para agrupar cambios rápidos al escribir cantidades.
    const timer = window.setTimeout(() => {
      void change<StockCheck[]>(path + "/availability", "POST", JSON.parse(payload), abort.signal).then((checks) => {
        if (!abort.signal.aborted) setResult({ key, checks });
      }).catch((failure: unknown) => {
        if (!abort.signal.aborted) setResult({ key, error: failure instanceof Error ? failure.message : "No se pudo consultar el almacén." });
      });
    }, 180);
    return () => {
      // Cancela la consulta anterior si cambian los datos o se cierra la pantalla.
      window.clearTimeout(timer);
      abort.abort();
    };
  }, [key, path, payload]);

  // Presenta el resultado solo si corresponde al carrito actual.
  return {
    checks: result?.key === key ? result.checks : undefined,
    error: result?.key === key ? result.error : undefined,
    loading: !!payload && result?.key !== key,
    retry: () => setAttempt((value) => value + 1)
  };
}

// Presenta pedido, disponible y faltante. Si el servidor indica un motivo, lo muestra junto a la cantidad que se está revisando.
export function StockFeedback({ row, unit, countUnit }: { row?: StockCheck; unit?: string; countUnit?: string }) {
  if (!row) return <>Disponibilidad por comprobar</>;
  const missing = Math.max(0, row.requested - row.available);
  const amount = (value: number) => quantityLabel(unit ? fromBase(value, unit) : value);
  const unitLabel = countUnit ?? (unit === "piece" ? "unidades" : unit ? unitLabels[unit] : "");
  return <span className={missing ? "trade-issue" : undefined}>
    {row.available === 0 && "Sin stock · "}{row.lot && "Lote " + row.lot + " · "}
    Pedido {amount(row.requested)} · Disponible {amount(row.available)}{unitLabel && " " + unitLabel}
    {missing > 0 && " · Faltan " + amount(missing)}{row.reason && " · " + row.reason}
  </span>;
}
