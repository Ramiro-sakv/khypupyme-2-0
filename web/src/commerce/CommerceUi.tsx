// Reúne controles de búsqueda, paginación, pago y formularios comerciales. Así Caja, Ventas y Catálogo comparten el mismo manejo de interacción.

import { useEffect, useRef, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import { Alert, Button, Card, EmptyState, SelectField, TextField } from "../ui/Primitives";
import { useCommerce } from "./CommerceContext";
import { methods } from "./models";
import type { CommandInput, CommandResult } from "./models";
import type { DraftStamp } from "./storage";

export function CommerceHeading({ title, children }: { title: string; children?: ReactNode }) {
  return <div className="page-heading commerce-heading">
    <h1 tabIndex={-1}>{title}</h1>{children}
  </div>;
}

export function LoadFeedback({ error, reload }: { error?: string; reload: () => void }) {
  return error ? <><Alert tone="error">{error}</Alert><Button variant="secondary" onClick={reload}>Reintentar</Button></>
    : <p role="status">Consultando registros…</p>;
}

export function NoRecords({ children = "Aquí aparecerán las operaciones confirmadas." }: { children?: ReactNode }) {
  return <EmptyState icon="document" title="Todavía no hay registros">{children}</EmptyState>;
}

// Usa hasMore de la respuesta para saber si hay otra página. Es suficiente para navegar sin calcular el total de filas en la interfaz.
export function Pages({ index, hasMore, onChange }: { index: number; hasMore?: boolean; onChange: (page: number) => void }) {
  if (index === 1 && !hasMore) return null;
  return <nav className="inline-actions pagination" aria-label="Páginas del listado">
    <Button variant="secondary" disabled={index <= 1} onClick={() => onChange(index - 1)}>Anterior</Button>
    <span>Página {index}</span>
    <Button variant="secondary" disabled={!hasMore} onClick={() => onChange(index + 1)}>Siguiente</Button>
  </nav>;
}

// Algunas pantallas consultan mientras se escribe y otras esperan el envío. La opción instant mantiene ambos recorridos con el mismo campo.
export function SearchForm({ label, onSearch, instant = false }: { label: string; onSearch: (query: string) => void; instant?: boolean }) {
  const [value, setValue] = useState("");
  const timer = useRef<number>(undefined);
  useEffect(() => () => window.clearTimeout(timer.current), []);
  return <form className="inline-actions commerce-search" onSubmit={(event) => {
    event.preventDefault();
    window.clearTimeout(timer.current);
    onSearch(value.trim());
  }}>
    <TextField label={label} value={value} type="search" maxLength={80} onChange={(event) => {
      const next = event.target.value;
      setValue(next);
      window.clearTimeout(timer.current);
      const term = next.trim();
      if (instant && term.length !== 1) timer.current = window.setTimeout(() => onSearch(term), 300);
    }} />
    <Button type="submit" variant="secondary" icon="search">Buscar</Button>
  </form>;
}

export function PaymentField({ value, onChange, credit = false, disabled }: {
  value: string; onChange: (value: string) => void; credit?: boolean; disabled?: boolean;
}) {
  return <SelectField label="Forma de pago" value={value} disabled={disabled} onChange={(event) => onChange(event.target.value)}>
    {!value && <option value="" disabled>Elige la forma de pago</option>}
    {Object.entries(methods).filter(([key]) => credit || key !== "credit")
      .map(([key, name]) => <option key={key} value={key}>{name}</option>)}
  </SelectField>;
}

// Conecta el formulario con el contexto del negocio y reúne el estado de envío, mensajes y resultado.
export function useCommercialForm(allowReadOnly = false) {
  const workspace = useCommerce();
  const [error, setError] = useState("");
  const [processing, setProcessing] = useState(false);

  async function submit(
    prepare: () => CommandInput | Promise<CommandInput>,
    complete: (result: CommandResult) => void | Promise<void>,
    stamp?: () => Promise<DraftStamp | undefined>,
  ) {
    setError("");
    setProcessing(true);
    try {
      // Prepara los datos y asegura el borrador antes de enviar el comando.
      // complete recibe el resultado confirmado y abre el comprobante si la pantalla lo pide.
      await complete(await workspace.execute(await prepare(), await stamp?.()));
    } catch (failure) {
      // Muestra el mensaje de validación o de red en el formulario.
      setError(failure instanceof Error ? failure.message : "No se pudo completar la operación.");
    } finally {
      setProcessing(false);
    }
  }

  // Desactiva los envíos mientras hay una operación en curso o pendiente.
  return {
    error,
    submit,
    processing,
    blocked: processing || workspace.busy || !!workspace.pending || !workspace.ready ||
      (workspace.readOnly && !allowReadOnly)
  };
}

// Recibe la captura de cada operación y sus estados de error o bloqueo para compartir el mismo pie de formulario.
export function OperationForm({ title, onClose, children, error, blocked, action = "Guardar", onSubmit }: {
  title: string; onClose?: () => void; children: ReactNode; error: string; blocked: boolean; action?: string;
  onSubmit: (event: FormEvent) => void;
}) {
  return <Card className="commerce-editor">
    <h2>{title}</h2>
    <form className="account-form" onSubmit={onSubmit}>
      {children}
      {error && <Alert tone="error">{error}</Alert>}
      <div className="inline-actions">
        {onClose && <Button variant="secondary" onClick={onClose}>Cancelar</Button>}
        <Button type="submit" icon="check" disabled={blocked}>{action}</Button>
      </div>
    </form>
  </Card>;
}
