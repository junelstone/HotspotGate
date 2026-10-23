// Licence codes, Machine IDs and Ed25519 signatures — the same formats as src/StonesHotspot.Licensing/Codes
// (Base32.cs, MachineId.cs, LicenceCode.cs, Ed25519.cs). Runs on Deno (Supabase) and Node 22+ (tests): only
// WebCrypto, no package.

export const BASE32_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

/** Decodes, ignoring dashes, spaces and case; null when a character is not in the alphabet. */
export function base32Decode(text: string): Uint8Array | null {
  const out: number[] = [];
  let buffer = 0, bits = 0;
  for (const raw of text) {
    if (raw === "-" || raw === " " || raw === "\r" || raw === "\n" || raw === "\t") continue;
    const value = BASE32_ALPHABET.indexOf(raw.toUpperCase());
    if (value < 0) return null;
    buffer = (buffer << 5) | value;
    bits += 5;
    if (bits >= 8) {
      out.push((buffer >> (bits - 8)) & 0xff);
      bits -= 8;
    }
    buffer &= (1 << bits) - 1;
  }
  return Uint8Array.from(out);
}

export function base32Encode(data: Uint8Array): string {
  let out = "", buffer = 0, bits = 0;
  for (const b of data) {
    buffer = (buffer << 8) | b;
    bits += 8;
    while (bits >= 5) {
      out += BASE32_ALPHABET[(buffer >> (bits - 5)) & 31];
      bits -= 5;
    }
    buffer &= (1 << bits) - 1;
  }
  if (bits > 0) out += BASE32_ALPHABET[(buffer << (5 - bits)) & 31];
  return out;
}

/** Four 15-bit hardware fingerprints: Windows, board, disk, CPU. 0 = not readable. */
export type MachineId = [number, number, number, number];

/** "MID-XXXX-XXXX-XXXX" → the four parts; null when malformed. */
export function parseMachineId(text: string | null | undefined): MachineId | null {
  const t = (text ?? "").trim().toUpperCase();
  if (!t.startsWith("MID-")) return null;
  const chars = t.slice(4).replaceAll("-", "").replaceAll(" ", "");
  if (chars.length !== 12) return null;
  const bytes = base32Decode(chars + "AAAA"); // 16 characters = 10 bytes
  return bytes ? machineIdFromBytes(bytes) : null;
}

export function machineIdFromBytes(bytes: Uint8Array): MachineId {
  let bits = 0n;
  for (let i = 0; i < 8; i++) bits = (bits << 8n) | BigInt(bytes[i]);
  bits >>= 4n;
  const part = (shift: bigint) => Number((bits >> shift) & 0x7fffn);
  return [part(45n), part(30n), part(15n), part(0n)];
}

export function formatMachineId(id: MachineId): string {
  let bits = (BigInt(id[0]) << 45n) | (BigInt(id[1]) << 30n) | (BigInt(id[2]) << 15n) | BigInt(id[3]);
  bits <<= 4n;
  const bytes = new Uint8Array(8);
  for (let i = 0; i < 8; i++) bytes[i] = Number((bits >> BigInt(56 - 8 * i)) & 0xffn);
  const text = base32Encode(bytes).slice(0, 12);
  return `MID-${text.slice(0, 4)}-${text.slice(4, 8)}-${text.slice(8, 12)}`;
}

/** True when `current` (the PC now) is the PC `licensed` (from a licence) was made for: 3 of 4 parts, or all readable ones. */
export function machineMatches(licensed: MachineId, current: MachineId): boolean {
  const readable = licensed.filter((p) => p !== 0).length;
  const matching = licensed.filter((p, i) => p !== 0 && p === current[i]).length;
  return readable > 0 && matching >= Math.min(3, readable);
}

// ---- Ed25519 (keys are the raw 32-byte seed / public key, base64, as LicenseManager shows them) ----

const PKCS8_ED25519_PREFIX = Uint8Array.from([0x30, 0x2e, 0x02, 0x01, 0x00, 0x30, 0x05, 0x06, 0x03, 0x2b, 0x65, 0x70, 0x04, 0x22, 0x04, 0x20]);

export function fromBase64(text: string): Uint8Array {
  const binary = atob(text.trim());
  return Uint8Array.from(binary, (c) => c.charCodeAt(0));
}

export function toBase64(bytes: Uint8Array): string {
  let binary = "";
  for (const b of bytes) binary += String.fromCharCode(b);
  return btoa(binary);
}

export function importPrivateKey(seed: Uint8Array): Promise<CryptoKey> {
  if (seed.length !== 32) throw new Error("The server private key must be 32 bytes (base64 from LicenseManager → Clés).");
  const pkcs8 = new Uint8Array(PKCS8_ED25519_PREFIX.length + 32);
  pkcs8.set(PKCS8_ED25519_PREFIX);
  pkcs8.set(seed, PKCS8_ED25519_PREFIX.length);
  return crypto.subtle.importKey("pkcs8", pkcs8, { name: "Ed25519" }, false, ["sign"]);
}

export function importPublicKey(raw: Uint8Array): Promise<CryptoKey> {
  return crypto.subtle.importKey("raw", raw, { name: "Ed25519" }, false, ["verify"]);
}

export async function sign(key: CryptoKey, data: Uint8Array): Promise<Uint8Array> {
  return new Uint8Array(await crypto.subtle.sign({ name: "Ed25519" }, key, data));
}

export function verify(key: CryptoKey, data: Uint8Array, signature: Uint8Array): Promise<boolean> {
  return crypto.subtle.verify({ name: "Ed25519" }, key, signature, data);
}

// ---- Licence codes ("SHS1-…") ----

export interface LicenceData {
  id: number;
  edition: "Basic" | "Pro";
  issuedOn: string; // yyyy-MM-dd
  expiresOn: string;
  machineId: string; // MID-…
  customer: string;
}

const EPOCH = Date.UTC(2024, 0, 1);
const DAY = 86_400_000;
const SIGNATURE_SIZE = 64;

function day(offset: number): string {
  return new Date(EPOCH + offset * DAY).toISOString().slice(0, 10);
}

/** Reads and checks a licence code (or .lic text). Null when it is not a well-formed code signed with the licence key. */
export async function verifyLicenceCode(text: string | null | undefined, licencePublicKey: CryptoKey): Promise<LicenceData | null> {
  if (!text || !text.trim()) return null;
  const joined = text.split("\n").filter((l) => !l.trimStart().startsWith("#")).join("").replace(/\s+/g, "");
  if (!joined.toUpperCase().startsWith("SHS1-")) return null;
  const bytes = base32Decode(joined.slice(5));
  if (!bytes || bytes.length < 20 + SIGNATURE_SIZE) return null;
  const payloadLength = 20 + bytes[19];
  if (bytes[0] !== 1 || bytes.length < payloadLength + SIGNATURE_SIZE || payloadLength - 20 > 32) return null;
  const payload = bytes.slice(0, payloadLength);
  if (!(await verify(licencePublicKey, payload, bytes.slice(payloadLength, payloadLength + SIGNATURE_SIZE)))) return null;

  const view = new DataView(payload.buffer, payload.byteOffset, payload.byteLength);
  const edition = payload[5] === 2 ? "Basic" : payload[5] === 3 ? "Pro" : null; // Edition enum: None, Trial, Basic, Pro
  if (!edition) return null;
  return {
    id: view.getUint32(1),
    edition,
    issuedOn: day(view.getUint16(6)),
    expiresOn: day(view.getUint16(8)),
    machineId: formatMachineId(machineIdFromBytes(payload.slice(10, 18))),
    customer: new TextDecoder().decode(payload.slice(20)),
  };
}
