// Centraliza las peticiones HTTP de React a KhypuPyme. Las pantallas reciben datos o un error con estado HTTP desde esta capa.

import { apiUrl, isNative } from "../native/platform";
import { nativeLogout, nativeToken } from "../native/session";

const auth = "/api/v1/auth";

// El estado permite diferenciar errores como una sesión vencida, una operación rechazada y un fallo de conexión.
export class ApiError extends Error {
  constructor(readonly status: number, message: string) {
    super(message.endsWith("[" + status + "]") ? message : message + " [" + status + "]");
  }
}

// Una petición web usa la sesión del navegador. En Android y Windows el cliente añade el token obtenido por el flujo nativo.
async function read<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers);
  const nativeSession = isNative && path === auth + "/session";
  const emptySession = { email: null, accountId: null, googleConfigured: false, googleLinked: false, localMailbox: false };
  if (isNative) {
    const token = await nativeToken();
    if (!token && nativeSession) return emptySession as T;
    if (!token) throw new ApiError(401, "Ingresa de nuevo para continuar.");
    headers.set("Authorization", "Bearer " + token);
  }
  const response = await fetch(apiUrl(nativeSession ? auth + "/native-session" : path), {
    ...init, headers, cache: "no-store", credentials: isNative ? "omit" : "same-origin",
    ...(isNative && { redirect: "error" }),
  }).catch((failure: unknown) => {
    if (init?.signal?.aborted || !(failure instanceof TypeError)) throw failure;
    throw new ApiError(503, "No se pudo conectar con KhypuPyme. Comprueba la conexión e inténtalo de nuevo.");
  });
  if (nativeSession && response.status === 401) {
    await nativeLogout(false);
    return emptySession as T;
  }
  // Si la API explica el fallo en detail, se presenta ese mensaje. El estado HTTP sigue disponible para que la pantalla pueda decidir qué hacer.
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string } | null;
    throw new ApiError(response.status, problem?.detail ?? ({
      401: "Ingresa de nuevo para continuar.",
      404: "No se encontró el negocio.",
      429: "Espera antes de volver a intentar.",
      503: "El servicio no está disponible. Comprueba la conexión y vuelve a consultar.",
    }[response.status] ?? "No se pudo completar la solicitud. Vuelve a intentar."));
  }
  if (response.status === 204) return undefined as T;
  return await response.json() as T;
}

// Usa la lectura compartida para las rutas que consultan datos. El tipo T describe la respuesta que espera el código de esa pantalla.
export function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  return read<T>(path, { signal });
}

// change prepara las peticiones POST y PUT que usan los formularios y el carrito.
export async function change<T>(path: string, method: "POST" | "PUT", body?: unknown, signal?: AbortSignal): Promise<T> {
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  // La sesión web envía un token de protección. La aplicación instalada usa su token de acceso.
  if (!isNative) headers["X-CSRF-TOKEN"] = (await get<{ token: string }>(auth + "/csrf", signal)).token;
  // read envía la petición HTTP y convierte los errores de la API en mensajes de pantalla.
  return read<T>(path, {
    method,
    headers,
    signal,
    // JSON es el formato que la API usa para recibir el comando.
    ...(body !== undefined && { body: JSON.stringify(body) }),
  });
}

// Inicia la vinculación por formulario porque la API continuará el flujo redirigiendo el navegador. El formulario lleva el token de protección de la sesión web.
export async function linkGoogle() {
  const { token } = await get<{ token: string }>(auth + "/csrf");
  const form = document.createElement("form");
  form.method = "POST";
  form.action = auth + "/google/link";
  const input = document.createElement("input");
  input.name = "__RequestVerificationToken";
  input.value = token;
  input.type = "hidden";
  form.append(input);
  document.body.append(form);
  form.submit();
}
