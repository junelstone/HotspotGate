// Runs the Edge Function's handler (Node 22+, in memory) through a scripted session and prints every answer as JSON.
// tests/StonesHotspot.Tests/SupabaseLicenceServerTests.cs feeds it keys and codes made by the C# side and checks the
// answers with the hotspot's own verification code. Input (stdin): see `Input` below.
import { createHandler } from "../supabase/functions/_shared/licence-api.ts";
import { MemoryStore } from "./memory-store.ts";

interface Input {
  serverPrivateKey: string;
  licencePublicKey: string;
  managerToken: string;
  machineId: string;
  otherMachineId: string;
  code: string;
  renewedCode: string;
  renewedUpload: Record<string, unknown>;
  revocationUpload: Record<string, unknown>;
  manifest: { version: string; url: string; sha256: string; notes: string; publishedAt: string };
}

const chunks: Buffer[] = [];
for await (const chunk of process.stdin) chunks.push(chunk as Buffer);
const input = JSON.parse(Buffer.concat(chunks).toString("utf8")) as Input;

const store = new MemoryStore();
const handle = createHandler({
  serverPrivateKey: input.serverPrivateKey, licencePublicKey: input.licencePublicKey, managerToken: input.managerToken,
  basePath: "/licence", rateLimit: 0,
}, store);
const base = "https://example.supabase.co/licence/";

async function call(method: string, path: string, body?: unknown, token?: string) {
  const headers: Record<string, string> = { "content-type": "application/json", "x-forwarded-for": "41.202.1.2" };
  if (token) headers.authorization = `Bearer ${token}`;
  const response = await handle(new Request(base + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) }));
  const text = await response.text();
  return { status: response.status, body: text ? JSON.parse(text) : null };
}

const results: Record<string, unknown> = {};
results.health = await call("GET", "health");
results.trial = await call("POST", "api/v1/trial", { machineId: input.machineId, firstRun: "2026-03-01T08:00:00Z", nonce: "n-trial", version: "1.0.0" });
results.trialLater = await call("POST", "api/v1/trial", { machineId: input.machineId, firstRun: "2026-05-01T08:00:00Z", nonce: "n-trial2", version: "1.0.0" });
results.badTrial = await call("POST", "api/v1/trial", { machineId: "nonsense", firstRun: "2026-03-01T08:00:00Z", nonce: "x" });
results.activate = await call("POST", "api/v1/activate", { code: input.code, machineId: input.machineId, nonce: "n-activate", version: "1.0.0" });
results.tampered = await call("POST", "api/v1/activate", { code: input.code.slice(0, 12) + (input.code[12] === "A" ? "B" : "A") + input.code.slice(13), machineId: input.machineId, nonce: "n" });
const licenceId = results.activate && (await JSON.parse(atob((results.activate as { body: { payload: string } }).body.payload))).licenceId;
results.otherPc = await call("POST", "api/v1/checkin", { licenceId, machineId: input.otherMachineId, expiresOn: "2027-03-01", nonce: "n-other", version: "1.0.0" });
results.uploadWrongToken = await call("POST", "api/v1/manager/licences", [input.renewedUpload], "wrong-token");
results.upload = await call("POST", "api/v1/manager/licences", [input.renewedUpload], input.managerToken);
results.renewed = await call("POST", "api/v1/checkin", { licenceId, machineId: input.machineId, expiresOn: "2027-03-01", nonce: "n-renew", version: "1.0.0" });
results.renewalGet = await call("GET", `api/v1/renewal/${licenceId}?machineId=${encodeURIComponent(input.machineId)}&expiresOn=2027-03-01&nonce=n-get`);
results.revoke = await call("POST", "api/v1/manager/licences", [input.revocationUpload], input.managerToken);
results.revoked = await call("POST", "api/v1/checkin", { licenceId, machineId: input.machineId, expiresOn: "2028-03-01", nonce: "n-revoked", version: "1.0.0" });
results.noUpdate = await call("GET", "api/v1/update");
store.manifest = { ...input.manifest };
results.update = await call("GET", "api/v1/update");
results.unknownPath = await call("GET", "api/v1/nothing");
results.checkinCount = store.checkins.length;

process.stdout.write(JSON.stringify(results));
