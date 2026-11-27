import { log } from "node:console";
import { writeFile } from "node:fs/promises";
import { URL } from "node:url";
import openapiTS, { astToString, COMMENT_HEADER } from "openapi-typescript";

// Cada nota se vincula con un encabezado generado. Si el contrato deja de publicarlo,
// se omite la nota junto con la declaración. Las propiedades se generan siempre desde OpenAPI.
const notes = new Map([
  ["export interface paths {", [
    "Este archivo .d.ts declara los datos que React espera de la API. TypeScript los usa al revisar el código.",
    "Se genera desde el contrato OpenAPI del servidor C#. En el navegador, fetch envía la petición y la API aplica las reglas.",
    "Empieza por GetApiHealth, CommerceCommand o StockCheck. Puedes buscarlos con Ctrl+F.",
    "",
    "CÓMO LEER LOS TIPOS",
    "string es texto, number es un número y boolean representa true o false.",
    "En email: null | string, la propiedad viene con un texto o null.",
    "En customerId?: null | string, ? indica que la propiedad también puede faltar.",
    "El sufijo [] señala una lista. components[\"schemas\"][\"CommandLine\"][] es una lista de líneas del carrito.",
    "never representa una opción que el contrato no declara. En una ruta de lectura, post?: never indica que el tipo no ofrece POST.",
    "Un campo marcado uuid sigue siendo texto para TypeScript. El servidor verifica su formato.",
    "",
    "RUTAS HTTP",
    "Cada clave es una ruta. GET suele consultar y POST suele enviar una acción. Un POST también puede consultar, como en availability.",
    "parameters.path indica los identificadores que se ponen en la URL y parameters.query los filtros que van después de ?.",
    "requestBody describe qué se envía en el cuerpo. responses describe qué se espera por cada código HTTP.",
  ]],
  ['"/api/v1/health/api": {', [
    "Ejemplo breve. Su GET remite a operations[\"GetApiHealth\"]. Busca ese nombre cerca del final.",
    "StatusPage.tsx usa la ruta, el método GET, la respuesta 200 y application/json para obtener su tipo HealthResponse.",
  ]],
  ['"/api/v1/health/database": {', [
    "Comprueba la conexión con PostgreSQL y que el esquema esté preparado. La operación nombrada incluye los estados HTTP 200 y 503.",
  ]],
  ['"/api/v1/auth/session": {', [
    "Devuelve SessionInfo. React lo lee para saber si ofrecer el ingreso o abrir el espacio de la cuenta.",
  ]],
  ['"/api/v1/businesses": {', [
    "En esta dirección GET lista negocios y POST solicita crear uno. Cada método tiene sus propios datos de entrada y salida.",
  ]],
  ['"/api/v1/businesses/{businessId}/plan": {', [
    "El texto entre llaves se reemplaza con el identificador del negocio real. Esta ruta consulta su licencia.",
  ]],
  ['"/api/v1/businesses/{businessId}/commerce/products": {', [
    "Comienzan las rutas del comercio habitual. businessId hace explícito a qué negocio se refiere la consulta.",
  ]],
  ['"/api/v1/businesses/{businessId}/commerce/availability": {', [
    "Antes de cobrar, React envía un StockRequest por POST y recibe una lista de StockCheck.",
    "Esta vista previa consulta lo disponible. La API comprueba el saldo nuevamente al confirmar la venta.",
  ]],
  ['"/api/v1/businesses/{businessId}/commerce/warehouse": {', [
    "Consulta las fichas de Almacén con filtros y paginación. La respuesta anuncia si queda otra página.",
  ]],
  ['"/api/v1/businesses/{businessId}/commerce/documents/{id}": {', [
    "Consulta un comprobante ya registrado. El detalle que llega como EntryDocument es el que puede pasarse al generador PDF.",
  ]],
  ['"/api/v1/businesses/{businessId}/commerce/commands/{id}": {', [
    "Consulta el resultado de un intento por su operationId. Sirve si la pantalla perdió la respuesta al confirmar.",
  ]],
  ['"/api/v1/businesses/{businessId}/commerce/commands": {', [
    "RECORRIDO DE UNA VENTA",
    "1. React pone el ID del negocio en la ruta y llama a POST.",
    "2. Envía JSON según CommerceCommand. En una venta, kind vale \"sale\" y lines contiene el carrito.",
    "3. El 200 anuncia una respuesta correcta con CommandResult, que identifica el intento y la venta registrada.",
    "Los tipos describen la forma publicada. Los rechazos y sus comprobaciones se gestionan en el servidor.",
  ]],
  ['"/api/v1/businesses/{businessId}/practice-commerce/products": {', [
    "Comienzan las rutas de práctica. Repiten gran parte del contrato comercial para que el mismo frontend pueda trabajar en ese espacio.",
  ]],
  ['"/api/v1/security/proof": {', [
    "Contrato de confirmación de identidad para acciones protegidas. La API verifica la identidad y el contexto de la solicitud.",
  ]],
  ["export type webhooks = Record<string, never>;", [
    "Los webhooks serían notificaciones HTTP definidas en el contrato. El generador declara aquí que este OpenAPI no anuncia ninguno.",
  ]],
  ["export interface components {", [
    "MODELOS REUTILIZABLES",
    "Un modelo describe los campos del JSON. CommerceCommand, SessionInfo y CashInfo se pueden consultar por nombre.",
    "En commerce/models.ts, components[\"schemas\"][\"CommerceCommand\"] recibe el alias Command para usarlo en el carrito.",
  ]],
  ["BusinessProfile: {", [
    "Ficha del negocio devuelta por la API, con sus datos, el sistema de medidas y la política operativa.",
  ]],
  ["CashInfo: {", [
    "Datos de la sesión de Caja. openingMinor es el fondo inicial, expectedMinor el efectivo esperado y countedMinor el efectivo contado.",
    "Los campos Minor van en centavos de boliviano. Por ejemplo, 1500000 equivale a Bs 15.000,00.",
  ]],
  ["CashStatus: {", [
    "La consulta devuelve una sesión con forma CashInfo o el valor null.",
  ]],
  ["CommandLine: {", [
    "Una línea incluye el producto y la cantidad. Puede añadir precio, medida, lote capturado dentro de measure o series cuando corresponde.",
  ]],
  ["CommandResult: {", [
    "El operationId identifica la solicitud. entityId identifica el registro creado o afectado, como la venta que se abrirá en el historial.",
  ]],
  ["CommerceCommand: {", [
    "Forma del JSON para registrar una operación. Se reutiliza para ventas, compras, fichas y otros comandos.",
    "operationId identifica el intento y kind indica qué se pide. Por ejemplo, una venta lleva kind igual a \"sale\".",
    "Las líneas apuntan a CommandLine. El servidor decide qué campos son obligatorios para el tipo de operación elegido.",
    "Un campo opcional se puede omitir. Un campo con null en su tipo admite que se envíe explícitamente vacío.",
  ]],
  ["CommerceReport: {", [
    "Respuesta de Informes. Agrupa el periodo, los totales, las cifras por día y los valores de inventario y fiados.",
  ]],
  ["DocumentLine: {", [
    "Forma de una línea del comprobante. Sirve para mostrar los datos históricos de la operación, incluidos importes y detalles de rubro.",
  ]],
  ["EntryDocument: {", [
    "Comprobante confirmado con número, fecha, importe y líneas. La pantalla y el PDF usan esta respuesta.",
  ]],
  ["ProductInfo: {", [
    "Ficha de catálogo para las pantallas. Los campos de medida, variante o serie dependen de qué clase de producto se consulta.",
  ]],
  ["SessionInfo: {", [
    "Datos básicos de la sesión actual. Si email es null, la interfaz puede mostrar el formulario de ingreso.",
  ]],
  ["StockCheck: {", [
    "Resultado de disponibilidad. requested indica lo pedido, available lo que hay y reason puede explicar por qué no se puede vender.",
    "Con 9 pedidas y 8 disponibles, TradePage puede mostrar 1 faltante y bloquear la confirmación de venta.",
  ]],
  ["StockLine: {", [
    "Línea de consulta de existencias. Indica producto y cantidad, con detalles opcionales de medida, lote o serie.",
  ]],
  ["StockRequest: {", [
    "Entrada de availability. Agrupa las líneas a comprobar y dice si es una recepción o una cotización.",
  ]],
  ["TimberDimensions: {", [
    "Largo, espesor y ancho en millonésimas de milímetro. La misma combinación identifica la medida aunque se capture en pulgadas y pies.",
  ]],
  ["export type $defs = Record<string, never>;", [
    "Espacio para definiciones adicionales. En este contrato está vacío.",
  ]],
  ["export interface operations {", [
    "OPERACIONES CON NOMBRE",
    "Algunas rutas apuntan aquí por su nombre de operación. Otras describen su método directamente dentro de paths.",
  ]],
  ["GetApiHealth: {", [
    "GET de estado de la API. requestBody?: never indica que no lleva cuerpo. En 200, application/json apunta al modelo HealthStatus.",
    "Ruta en paths → GET → GetApiHealth → responses[200] → content[\"application/json\"] → HealthStatus.",
  ]],
  ["GetDatabaseHealth: {", [
    "GET de estado de PostgreSQL. Mira cómo 200 y 503 pueden compartir el modelo HealthStatus para explicar resultados distintos.",
  ]],
]);

const input = new URL("../../server/KhypuPyme.Api/obj/openapi/khypupyme.json", import.meta.url);
const output = new URL("../src/api/schema.d.ts", import.meta.url);
const declarations = astToString(await openapiTS(input));

const annotated = declarations.split("\n").flatMap((line) => {
  const comment = notes.get(line.trim());
  if (!comment) return [line];

  const indent = line.slice(0, line.length - line.trimStart().length);
  return [...comment.map((text) => text ? indent + "// " + text : indent + "//"), line];
});

await writeFile(output, COMMENT_HEADER + annotated.join("\n"), "utf8");
log("Tipos OpenAPI y comentarios de lectura generados en src/api/schema.d.ts.");
