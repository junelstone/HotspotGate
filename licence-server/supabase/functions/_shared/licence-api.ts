// The licence server's endpoints, as in src/StonesHotspot.LicenseServer/LicenceServerApi.cs, for a Supabase Edge
// Function. Every answer to a hotspot is signed with the server key; the hotspot checks signature, nonce and Machine ID.
// Storage is behind `Store` (Postgres on Supabase, memory in tests).
import {
  fromBase64, importPrivateKey, importPublicKey, machineMatches, parseMachineId, sign, toBase64, verifyLicenceCode,
} from "./codes.ts";

export interface StoredLicence {
  id: number;
  customer: string;
  machineId: string;
  edition: string;
  issuedOn: string; // yyyy-MM-dd
  expiresOn: string;
  code: string;
  revokedAt: string | null;
}

export interface Manifest {
  version: string;
  url: string;
  sha256: string;
  notes: string | null;
  publishedAt: string;
}

export interface Store {
  findLicence(id: number): Promise<StoredLicence | null>;
  upsertLicences(licences: StoredLicence[]): Promise<void>;
  /** Returns the earliest first run known for this PC. */
  registerTrial(machineId: string, firstRun: string, now: string, version: string | null): Promise<string>;
  recordCheckIn(c: { licenceId: number | null; machineId: string; kind: string; result: string; at: string; ip: string | null; version: string | null }): Promise<void>;
  updateManifest(): Promise<Manifest | null>;
}

export interface Config {
  serverPrivateKey: string; // base64 seed (LicenseManager → Clés)
  licencePublicKey: string; // base64
  managerToken: string;
  /** Path prefix in front of /api/v1 (on Supabase: "/licence", the function's name). */
  basePath?: string;
  now?: () => Date;
  /** Requests per minute per address on the public API (0 = no limit). */
  rateLimit?: number;
}

export const Status = { Valid: "valid", Revoked: "revoked", Renewed: "renewed", Unknown: "unknown", TrialRegistered: "trial-registered" } as const;

const UPDATE_LABEL = new TextEncoder().encode("StonesHotspot.Update.v1\n");

export function createHandler(config: Config, store: Store): (request: Request) => Promise<Response> {
  const keys = Promise.all([importPrivateKey(fromBase64(config.serverPrivateKey)), importPublicKey(fromBase64(config.licencePublicKey))]);
  const now = config.now ?? (() => new Date());
  const hits = new Map<string, { count: number; windowStart: number }>();

  const json = (body: unknown, status = 200) =>
    new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json; charset=utf-8" } });
  const empty = (status: number) => new Response(null, { status });

  async function signed(answer: Record<string, unknown>): Promise<Response> {
    const [serverKey] = await keys;
    const payload = new TextEncoder().encode(JSON.stringify(answer));
    return json({ payload: toBase64(payload), signature: toBase64(await sign(serverKey, payload)) });
  }

  function limited(ip: string): boolean {
    const limit = config.rateLimit ?? 30;
    if (limit <= 0) return false;
    const t = now().getTime();
    const entry = hits.get(ip);
    if (!entry || t - entry.windowStart > 60_000) {
      hits.set(ip, { count: 1, windowStart: t });
      if (hits.size > 10_000) hits.clear();
      return false;
    }
    return ++entry.count > limit;
  }

  async function answer(licenceId: number, machineIdText: string, clientExpiry: string, nonce: string, kind: string,
    version: string | null, ip: string | null): Promise<Response> {
    const at = now().toISOString();
    const licence = await store.findLicence(licenceId);
    const machine = parseMachineId(machineIdText);
    const licensed = licence ? parseMachineId(licence.machineId) : null;
    let status: string;
    if (!licence || !machine || !licensed || !machineMatches(licensed, machine)) status = Status.Unknown;
    else if (licence.revokedAt) status = Status.Revoked;
    else if (licence.expiresOn > clientExpiry) status = Status.Renewed;
    else status = Status.Valid;

    await store.recordCheckIn({ licenceId, machineId: machineIdText, kind, result: status, at, ip, version });
    return signed({
      status, licenceId, machineId: machineIdText, nonce, serverTime: at,
      revokedAt: status === Status.Revoked ? licence!.revokedAt : null,
      renewedCode: status === Status.Renewed ? licence!.code : null,
      trialFirstRun: null,
    });
  }

  async function body(request: Request): Promise<Record<string, unknown> | null> {
    try {
      const value = await request.json();
      return value && typeof value === "object" ? value as Record<string, unknown> : null;
    } catch {
      return null;
    }
  }

  const text = (v: unknown) => (typeof v === "string" ? v : null);

  return async (request: Request): Promise<Response> => {
    const url = new URL(request.url);
    let path = url.pathname;
    const base = config.basePath ?? "";
    if (base && path.startsWith(base)) path = path.slice(base.length);
    path = path.replace(/\/+$/, "") || "/";
    const ip = (request.headers.get("x-forwarded-for") ?? "").split(",")[0].trim() || null;
    const method = request.method.toUpperCase();

    try {
      if (method === "GET" && path === "/health") return json({ status: "ok" });

      if (path === "/api/v1/manager/licences" && method === "POST") {
        if (!(await tokenMatches(request.headers.get("authorization"), config.managerToken))) return empty(401);
        const uploads = await request.json().catch(() => null);
        if (!Array.isArray(uploads)) return empty(400);
        await store.upsertLicences(uploads.map((u) => ({
          id: Number(u.id), customer: String(u.customer ?? ""), machineId: String(u.machineId ?? ""), edition: String(u.edition ?? ""),
          issuedOn: String(u.issuedOn), expiresOn: String(u.expiresOn), code: String(u.code ?? ""), revokedAt: u.revokedAt ?? null,
        })));
        return json({ received: uploads.length });
      }

      if (!path.startsWith("/api/v1/")) return empty(404);
      if (limited(ip ?? "?")) return empty(429);

      if (method === "POST" && path === "/api/v1/trial") {
        const r = await body(request);
        const machineId = text(r?.machineId);
        const nonce = text(r?.nonce);
        const firstRun = Date.parse(text(r?.firstRun) ?? "");
        if (!r || !parseMachineId(machineId) || !nonce || Number.isNaN(firstRun)) return empty(400);
        const at = now();
        const first = await store.registerTrial(machineId!, new Date(Math.min(firstRun, at.getTime())).toISOString(), at.toISOString(), text(r.version));
        await store.recordCheckIn({ licenceId: null, machineId: machineId!, kind: "trial", result: Status.TrialRegistered, at: at.toISOString(), ip, version: text(r.version) });
        return signed({
          status: Status.TrialRegistered, licenceId: null, machineId, nonce, serverTime: at.toISOString(),
          revokedAt: null, renewedCode: null, trialFirstRun: first,
        });
      }

      if (method === "POST" && path === "/api/v1/activate") {
        const r = await body(request);
        const [, licenceKey] = await keys;
        const licence = await verifyLicenceCode(text(r?.code), licenceKey);
        const machineId = text(r?.machineId);
        const nonce = text(r?.nonce);
        if (!r || !licence || !parseMachineId(machineId) || !nonce) return empty(400);
        // Codes made while LicenseManager was offline become known here.
        const known = await store.findLicence(licence.id);
        if (!known || known.issuedOn < licence.issuedOn)
          await store.upsertLicences([{ ...licence, code: text(r.code)!.trim(), revokedAt: null }]);
        return answer(licence.id, machineId!, licence.expiresOn, nonce, "activate", text(r.version), ip);
      }

      if (method === "POST" && path === "/api/v1/checkin") {
        const r = await body(request);
        const licenceId = Number(r?.licenceId);
        if (!r || !Number.isInteger(licenceId) || !text(r.machineId) || !text(r.nonce) || !/^\d{4}-\d{2}-\d{2}$/.test(text(r.expiresOn) ?? ""))
          return empty(400);
        return answer(licenceId, text(r.machineId)!, text(r.expiresOn)!, text(r.nonce)!, "checkin", text(r.version), ip);
      }

      const renewal = path.match(/^\/api\/v1\/renewal\/(\d+)$/);
      if (method === "GET" && renewal) {
        const q = url.searchParams;
        if (!q.get("machineId") || !q.get("nonce") || !/^\d{4}-\d{2}-\d{2}$/.test(q.get("expiresOn") ?? "")) return empty(400);
        return answer(Number(renewal[1]), q.get("machineId")!, q.get("expiresOn")!, q.get("nonce")!, "renewal", null, ip);
      }

      // Latest version for the hotspots' daily update check (signed, so a fake server cannot push an installer).
      if (method === "GET" && path === "/api/v1/update") {
        const manifest = await store.updateManifest();
        if (!manifest || !wellFormed(manifest)) return empty(204);
        const [serverKey] = await keys;
        const payload = new TextEncoder().encode(JSON.stringify({
          version: manifest.version, url: manifest.url, sha256: manifest.sha256.toLowerCase(), notes: manifest.notes,
          publishedAt: manifest.publishedAt,
        }));
        const data = new Uint8Array(UPDATE_LABEL.length + payload.length);
        data.set(UPDATE_LABEL);
        data.set(payload, UPDATE_LABEL.length);
        return json({ payload: toBase64(payload), signature: toBase64(await sign(serverKey, data)) });
      }

      return empty(404);
    } catch (error) {
      console.error("licence server error", error instanceof Error ? error.message : error);
      return empty(500);
    }
  };
}

export function wellFormed(m: Manifest): boolean {
  let https = false;
  try {
    https = new URL(m.url).protocol === "https:";
  } catch { /* not a URL */ }
  return https && /^[0-9a-fA-F]{64}$/.test(m.sha256) && /^v?\d+(\.\d+){1,3}([+-].*)?$/i.test(m.version.trim());
}

/** Bearer token check that takes the same time whatever the token (hashes compared). */
async function tokenMatches(header: string | null, secret: string): Promise<boolean> {
  if (!secret || !header || !header.toLowerCase().startsWith("bearer ")) return false;
  const digest = async (s: string) => new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(s)));
  const [a, b] = await Promise.all([digest(header.slice(7).trim()), digest(secret)]);
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a[i] ^ b[i];
  return diff === 0;
}
