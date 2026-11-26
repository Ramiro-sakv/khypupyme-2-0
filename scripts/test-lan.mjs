import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { randomUUID, X509Certificate } from "node:crypto";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { getCACertificates, setDefaultCACertificates } from "node:tls";
import { setTimeout } from "node:timers/promises";

const { owner, setup, lan, certificate } = JSON.parse(readFileSync(0, "utf8"));
const email = "lan-" + randomUUID() + "@example.test";
const password = "Cuenta de red local para comprobar el piloto.";
const auth = "/api/v1/auth";
const businesses = "/api/v1/businesses";

function client(origin) {
  const cookies = new Map();
  async function request(path, expected, body) {
    const post = body !== undefined;
    const headers = {};
    if (post) {
      headers["X-CSRF-TOKEN"] = (await request(auth + "/csrf", 200)).token;
      headers["Content-Type"] = "application/json";
    }
    if (cookies.size) headers.Cookie = [...cookies.values()].join("; ");
    const response = await fetch(origin + path, { method: post ? "POST" : "GET", headers,
      body: post ? JSON.stringify(body) : undefined, signal: AbortSignal.timeout(5000) });
    for (const cookie of response.headers.getSetCookie()) {
      const pair = cookie.split(";")[0];
      if (origin.startsWith("https:")) assert.match(cookie, /; secure(?:;|$)/i);
      cookies.set(pair.slice(0, pair.indexOf("=")), pair);
    }
    const data = response.headers.get("content-type")?.includes("json") ? await response.json() : await response.text();
    assert.equal(response.status, expected, path + " · " + (data?.detail ?? response.statusText));
    return data;
  }
  return request;
}

async function ready(origin) {
  for (let attempt = 0; attempt < 40; attempt++) {
    try {
      if (origin === lan && existsSync(certificate)) {
        const pem = new X509Certificate(readFileSync(certificate)).toString();
        setDefaultCACertificates([...getCACertificates("default"), pem]);
      }
      const response = await fetch(origin + "/api/v1/health/database", { signal: AbortSignal.timeout(1000) });
      if (response.ok) return;
    } catch {
      // El proceso de pruebas puede seguir iniciando.
    }
    await setTimeout(300);
  }
  throw Error("No arrancó " + origin);
}

try {
  await Promise.all([ready(setup), ready(lan)]);
  const provision = client(setup);
  await provision(auth + "/register/code", 204, { email });
  const directory = process.env.Email__MailboxPath;
  const messages = readdirSync(directory).filter((file) => file.endsWith(".json"))
    .map((file) => JSON.parse(readFileSync(join(directory, file), "utf8")));
  await provision(auth + "/register", 204, { email, password, code: messages.find((message) => message.email === email).code });

  const pc = client(lan);
  const mobile = client(lan);
  assert.match(await pc("/", 200), /KhypuPyme/);
  const session = await mobile(auth + "/session", 200);
  assert.deepEqual([session.localNetwork, session.localMailbox, session.googleConfigured], [true, false, false]);
  await mobile(auth + "/register/code", 503, { email });
  await mobile(auth + "/recover/code", 503, { email });
  await mobile("/swagger/index.html", 404);
  await mobile(businesses, 401);
  await Promise.all([pc(auth + "/login", 204, { email, password }), mobile(auth + "/login", 204, { email, password })]);

  const id = randomUUID();
  const business = businesses + "/" + id;
  const scope = business + "/commerce";
  await pc(businesses, 200, { id, name: "Sicomoro LAN QA", industry: "timber", timberUnits: "imperial" });
  await pc(business + "/plan/trial", 204, {});
  const command = (device, body, status = 200) =>
    device(scope + "/commands", status, { operationId: randomUUID(), ...body });
  await command(pc, { kind: "open-cash", amountMinor: 200_000 });
  const { entityId: productId } = await command(pc, { kind: "product", name: "Tabla seca",
    timberProduct: { species: "Pino", superShortMinor: 800, shortMinor: 900, longMinor: 1000 } });
  const line = (quantity, unitMinor, width = 6) => ({ productId, quantity, unitMinor,
    timber: { lengthMicros: 3_048_000_000, thicknessMicros: 50_800_000, widthMicros: width * 25_400_000 } });
  const buy = (lines, method = "cash") => ({ kind: "purchase", method, lines });
  const sell = (lines) => ({ kind: "sale", method: "cash", lines });
  await command(pc, buy([line(10, 5000)]));
  await Promise.all([command(pc, buy([line(5, 5000)])), command(mobile, sell([line(2, 1000)]))]);
  const stock = async (device, width) => (await device(scope + "/timber-stock/" + productId +
    "?width=" + width * 25_400_000, 200)).items.reduce((total, row) => total + row.stock, 0);
  const cash = async (device) => (await device(scope + "/cash", 200)).session.expectedMinor;
  for (const device of [pc, mobile]) {
    assert.equal(await stock(device, 6), 13);
    assert.equal(await cash(device), 145_000);
  }
  console.log("Dos sesiones HTTPS confirmaron compra +5 y venta -2. Ambas ven 13 tablas y Bs 1.450 de efectivo.");

  await command(pc, buy([line(1, 5000, 8)], "transfer"));
  const sale = sell([line(1, 1000, 8)]);
  for (const device of [pc, mobile]) {
    assert.equal((await device(scope + "/availability", 200, { lines: sale.lines }))[0].available, 1);
  }
  const requests = [randomUUID(), randomUUID()];
  const competing = await Promise.allSettled([pc, mobile].map((device, index) =>
    command(device, { ...sale, operationId: requests[index] })));
  assert.equal(competing.filter((result) => result.status === "fulfilled").length, 1);
  const rejected = competing.findIndex((result) => result.status === "rejected");
  assert.equal(competing[rejected].reason.actual, 409);
  assert.match(competing[rejected].reason.message, /disponibles 0, faltan 1/);
  await pc(scope + "/commands/" + requests[rejected], 404);
  assert.equal(await stock(mobile, 8), 0);
  assert.equal(await stock(pc, 6), 13);
  assert.equal(await cash(mobile), 158_333); // Bs 1.450 + una tabla de 13⅓ pies tablares a Bs 10 por pie.
  assert.equal((await pc(scope + "/history?kind=sale", 200)).items.length, 2);
  console.log("Última unidad: una venta confirmada, HTTP 409 en la otra y un solo movimiento de Caja.");

  const devices = [pc, mobile, client(lan), client(lan), client(lan)];
  await Promise.all(devices.slice(2).map((device) => device(auth + "/login", 204, { email, password })));
  await command(pc, buy([line(3, 5000, 9)], "transfer"));
  const lastThree = sell([line(1, 1000, 9)]);
  const fiveRequests = devices.map(() => randomUUID());
  const fiveResults = await Promise.allSettled(devices.map((device, index) =>
    command(device, { ...lastThree, operationId: fiveRequests[index] })));
  assert.equal(fiveResults.filter((result) => result.status === "fulfilled").length, 3);
  for (const [index, result] of fiveResults.entries()) {
    if (result.status !== "rejected") continue;
    assert.equal(result.reason.actual, 409);
    await devices[index](scope + "/commands/" + fiveRequests[index], 404);
  }
  for (const device of devices) {
    assert.equal(await stock(device, 9), 0);
    assert.equal(await cash(device), 203_333);
  }
  assert.equal((await pc(scope + "/history?kind=sale", 200)).items.length, 5);
  console.log("Cinco sesiones HTTPS disputaron tres tablas: tres ventas, dos rechazos, un saldo compartido.");
} finally {
  execFileSync(process.env.KHYPU_TEST_PSQL,
    ["-X", "-w", "-h", "127.0.0.1", "-U", "khypupyme2_test_owner", "-d", "khypupyme2_test", "-v", "ON_ERROR_STOP=1",
      "-c", `DELETE FROM khypupyme."AspNetUsers" WHERE "NormalizedEmail"=upper('${email}');
             DELETE FROM khypupyme.email_challenges WHERE "Email"=upper('${email}');`],
    { env: { ...process.env, PGPASSWORD: owner }, stdio: ["ignore", "ignore", "pipe"] });
}
