// In-memory Store with the same rules as the SQL functions (earliest first run kept, revocation never undone).
import type { Manifest, Store, StoredLicence } from "../supabase/functions/_shared/licence-api.ts";

export class MemoryStore implements Store {
  licences = new Map<number, StoredLicence>();
  trials = new Map<string, string>();
  checkins: unknown[] = [];
  manifest: Manifest | null = null;

  findLicence(id: number) {
    return Promise.resolve(this.licences.get(id) ?? null);
  }
  upsertLicences(licences: StoredLicence[]) {
    for (const l of licences) {
      const known = this.licences.get(l.id);
      this.licences.set(l.id, { ...l, revokedAt: l.revokedAt ?? known?.revokedAt ?? null });
    }
    return Promise.resolve();
  }
  registerTrial(machineId: string, firstRun: string) {
    const known = this.trials.get(machineId);
    const first = known && Date.parse(known) < Date.parse(firstRun) ? known : firstRun;
    this.trials.set(machineId, first);
    return Promise.resolve(first);
  }
  recordCheckIn(c: unknown) {
    this.checkins.push(c);
    return Promise.resolve();
  }
  updateManifest() {
    return Promise.resolve(this.manifest);
  }
}
