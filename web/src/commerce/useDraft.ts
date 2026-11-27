// Hook de React que conserva el borrador en el navegador. La captura se puede recuperar al volver a la operación.

import { useCallback, useEffect, useRef, useState } from "react";
import { readDraft, saveDraft } from "./storage";
import type { DraftStamp } from "./storage";

type DraftView<T> = { value: T; ready: boolean; error: string };

export function useDraft<T>(key: string, initial: T) {
  const base = useRef(initial);
  const [view, setView] = useState<DraftView<T>>({ value: initial, ready: false, error: "" });
  const value = useRef(initial);
  const saved = useRef<DraftStamp | undefined>(undefined);
  // Guarda las escrituras en orden para que una edición lenta no sustituya lo que el usuario escribió después.
  const queue = useRef(Promise.resolve());
  const lastError = useRef("");
  const generation = useRef({ value: 0 });

  // Espera las escrituras pendientes y vuelve a mirar IndexedDB. La generación protege contra una lectura que termine cuando la vista ya cambió.
  const reload = useCallback(async () => {
    const revision = ++generation.current.value;
    try {
      await queue.current;
      const stored = await readDraft<T>(key);
      if (revision !== generation.current.value) return;
      saved.current = stored;
      value.current = stored?.value ?? base.current;
      lastError.current = "";
      setView({ value: value.current, ready: true, error: "" });
    } catch {
      if (revision === generation.current.value)
        setView((current) => ({ ...current, error: "No se pudo abrir el borrador local." }));
    }
  }, [key]);

  useEffect(() => {
    const lifetime = generation.current;
    void reload();
    return () => { lifetime.value++; };
  }, [reload]);

  // Muestra el cambio de inmediato y guarda una copia local. La revisión evita sobrescribir cambios guardados por otra pestaña.
  function update(produce: (draft: T) => T) {
    const next = produce(value.current);
    value.current = next;
    setView((current) => ({ ...current, value: next }));
    queue.current = queue.current.then(async () => {
      try {
        saved.current = await saveDraft(key, next, saved.current?.revision);
        lastError.current = "";
      } catch (error) {
        lastError.current = error instanceof Error ? error.message : "No se pudo guardar el borrador.";
      }
      setView((current) => ({ ...current, error: lastError.current }));
    });
  }

  // Antes de enviar una operación espera a que el borrador esté guardado. Devuelve la revisión exacta que pertenece al intento.
  async function flush() {
    await queue.current;
    if (lastError.current) throw new Error(lastError.current);
    return saved.current;
  }

  return { ...view, update, flush, reload };
}
