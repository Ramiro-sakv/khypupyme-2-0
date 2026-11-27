// Administra IndexedDB para los borradores y las solicitudes en curso. Cada clase de registro tiene sus claves y reglas de actualización.

import type { Command } from "./models";

export type DraftStamp = { key: string; revision: string };
export type DraftRecord<T> = DraftStamp & { value: T };
export type PendingRecord = { scope: string; command: Command; draft?: DraftStamp };
type Store = "drafts" | "pending";

let opened: Promise<IDBDatabase> | undefined;
// Abre el almacenamiento del navegador. En una primera visita se crean los almacenes que usan los formularios.
function database() {
  opened ??= new Promise<IDBDatabase>((resolve, reject) => {
    const request = indexedDB.open("khypupyme2-commerce", 1);
    request.onupgradeneeded = () => {
      request.result.createObjectStore("drafts");
      request.result.createObjectStore("pending");
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => { opened = undefined; reject(request.error); };
  });
  return opened;
}

// Ejecuta la lectura y el cambio bajo la misma transacción local. Revisar la revisión y guardar por separado permitiría que dos pestañas se pisaran.
async function edit<T, R>(name: Store, key: string, mode: IDBTransactionMode,
  apply: (store: IDBObjectStore, value: T | undefined, transaction: IDBTransaction) => R,
  writableScope?: string): Promise<R> {
  const db = await database();
  const stores: Store[] = mode === "readwrite" && (name === "pending" || writableScope) ? ["pending", "drafts"] : [name];
  return new Promise((resolve, reject) => {
    const tx = db.transaction(stores, mode);
    const store = tx.objectStore(name);
    const request = store.get(key);
    let result: R;
    let failure: unknown;
    tx.oncomplete = () => resolve(result);
    tx.onabort = () => reject(failure ?? tx.error);
    function applyChange() {
      try { result = apply(store, request.result as T | undefined, tx); }
      catch (error) { failure = error; tx.abort(); }
    }
    request.onsuccess = () => {
      if (!writableScope) { applyChange(); return; }
      const retired = tx.objectStore("pending").get("retired:" + writableScope);
      retired.onsuccess = () => {
        if (retired.result) { failure = new Error("Este espacio ya fue eliminado."); tx.abort(); return; }
        const closing = tx.objectStore("pending").get("closing:" + writableScope);
        closing.onsuccess = () => {
          if (closing.result) { failure = new Error("La confirmación del borrado está en curso."); tx.abort(); }
          else applyChange();
        };
      };
    };
  });
}

export function readDraft<T>(key: string) {
  return edit<DraftRecord<T>, DraftRecord<T> | undefined>("drafts", key, "readonly", (_, value) => value);
}

// Compara la versión esperada con la guardada antes de reemplazar el borrador.
export function saveDraft<T>(key: string, value: T, expected: string | undefined) {
  return edit<DraftRecord<T>, DraftStamp>("drafts", key, "readwrite", (store, current) => {
    if (current?.revision !== expected) throw new Error("El borrador guardado cambió en otra ventana. Carga su versión antes de continuar.");
    const stamp = { key, revision: crypto.randomUUID() };
    store.put({ ...stamp, value }, key);
    return stamp;
  }, key.split(":").slice(0, 2).join(":"));
}

export function pendingRequest(scope: string) {
  return edit<PendingRecord, PendingRecord | undefined>("pending", scope, "readonly", (_, value) => value);
}

export async function pendingForAccount(accountId: string) {
  const db = await database();
  const prefix = accountId + ":";
  return new Promise<boolean>((resolve, reject) => {
    const query = db.transaction("pending", "readonly").objectStore("pending")
      .count(IDBKeyRange.bound(prefix, prefix + "\uffff"));
    query.onsuccess = () => resolve(query.result > 0);
    query.onerror = () => reject(query.error);
  });
}

export function clearPracticeScope(scope: string) {
  return edit<PendingRecord, void>("pending", scope, "readwrite", (store, _, tx) => {
    store.delete(scope);
    store.put(true, "retired:" + scope);
    store.delete("closing:" + scope);
    const drafts = tx.objectStore("drafts");
    drafts.delete(IDBKeyRange.bound(scope + ":", scope + ":\uffff"));
  });
}

// Marca el contexto que se está preparando para borrar. Así otro flujo local puede saber que hay una baja en curso.
export function freezeRemoval(scope: string) {
  return edit<PendingRecord, void>("pending", scope, "readwrite", (store, pending) => {
    if (pending) throw new Error("Consulta la operación pendiente de este navegador antes de borrar.");
    store.put(true, "closing:" + scope);
  });
}

export function releaseRemoval(scope: string) {
  return edit<PendingRecord, void>("pending", scope, "readwrite", (store) => { store.delete("closing:" + scope); });
}

export function removalIsFrozen(scope: string) {
  return edit<boolean, boolean>("pending", "closing:" + scope, "readonly", (_, value) => !!value);
}

// Restringe los envíos sin resolver del mismo contexto. Una segunda pestaña obtiene el registro existente en lugar de tomar su lugar.
export function reserveRequest(record: PendingRecord) {
  return edit<PendingRecord, PendingRecord>("pending", record.scope, "readwrite", (store, current) => {
    if (current) return current;
    store.add(record, record.scope);
    return record;
  }, record.scope);
}

// Al confirmar se puede retirar el borrador asociado. La revisión protege un borrador posterior aunque pertenezca a la misma pantalla.
export function clearRequest(record: PendingRecord, confirmed: boolean) {
  return edit<PendingRecord, void>("pending", record.scope, "readwrite", (store, current, tx) => {
    if (current?.command.operationId !== record.command.operationId) return;
    store.delete(record.scope);
    const stamp = current.draft;
    if (!confirmed || !stamp) return;
    const drafts = tx.objectStore("drafts");
    const get = drafts.get(stamp.key);
    get.onsuccess = () => {
      if ((get.result as DraftStamp | undefined)?.revision === stamp.revision) drafts.delete(stamp.key);
    };
  });
}
