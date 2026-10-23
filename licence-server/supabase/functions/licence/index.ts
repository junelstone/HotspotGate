// Supabase Edge Function "licence": https://<project>.supabase.co/functions/v1/licence/ is the hotspots'
// Licensing:ServerUrl and LicenseManager's server address. Secrets (supabase secrets set …):
//   SERVER_PRIVATE_KEY  the owner's server private key (LicenseManager → Clés), base64
//   LICENCE_PUBLIC_KEY  the owner's licence public key, base64
//   MANAGER_TOKEN       long random text, also typed in LicenseManager → Serveur
// SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY are provided by Supabase.
import { createClient } from "jsr:@supabase/supabase-js@2";
import { createHandler, type Manifest, type Store, type StoredLicence } from "../_shared/licence-api.ts";

function required(name: string): string {
  const value = Deno.env.get(name);
  if (!value) throw new Error(`Secret ${name} is missing: supabase secrets set ${name}=…`);
  return value;
}

const db = createClient(required("SUPABASE_URL"), required("SUPABASE_SERVICE_ROLE_KEY"), {
  auth: { persistSession: false, autoRefreshToken: false },
});

function check<T>(result: { data: T; error: { message: string } | null }): T {
  if (result.error) throw new Error(result.error.message);
  return result.data;
}

const store: Store = {
  async findLicence(id) {
    const row = check(await db.from("licences").select("*").eq("id", id).maybeSingle());
    return row
      ? {
        id: Number(row.id), customer: row.customer, machineId: row.machine_id, edition: row.edition,
        issuedOn: row.issued_on, expiresOn: row.expires_on, code: row.code, revokedAt: row.revoked_at,
      } satisfies StoredLicence
      : null;
  },
  async upsertLicences(licences) {
    check(await db.rpc("upsert_licences", { p_licences: licences }));
  },
  async registerTrial(machineId, firstRun, now, version) {
    const first = check(await db.rpc("register_trial", { p_machine_id: machineId, p_first_run: firstRun, p_now: now, p_version: version }));
    return new Date(first as string).toISOString();
  },
  async recordCheckIn(c) {
    check(await db.from("checkins").insert({
      licence_id: c.licenceId, machine_id: c.machineId, kind: c.kind, result: c.result, at: c.at, ip: c.ip, version: c.version,
    }));
  },
  async updateManifest() {
    const row = check(await db.from("update_manifest").select("*").eq("id", 1).maybeSingle());
    return row && row.version
      ? { version: row.version, url: row.url, sha256: row.sha256, notes: row.notes, publishedAt: new Date(row.published_at).toISOString() } satisfies Manifest
      : null;
  },
};

const handler = createHandler({
  serverPrivateKey: required("SERVER_PRIVATE_KEY"),
  licencePublicKey: required("LICENCE_PUBLIC_KEY"),
  managerToken: required("MANAGER_TOKEN"),
  basePath: "/licence",
}, store);

Deno.serve(handler);
