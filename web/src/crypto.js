// מפתחות נגזרים מה-Client Secret (בלי משתנה סודי נוסף להגדרה)
const enc = new TextEncoder();

export const b64url = (bytes) =>
  btoa(String.fromCharCode(...new Uint8Array(bytes))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
export const fromB64url = (s) =>
  Uint8Array.from(atob(s.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((s.length + 3) % 4)), (c) => c.charCodeAt(0));
export const randomId = (n = 24) => b64url(crypto.getRandomValues(new Uint8Array(n)));

const keyCache = new Map();
async function derive(env, purpose, usage) {
  const cacheKey = purpose + ":" + (env.KICK_CLIENT_SECRET || "").length;
  if (keyCache.has(cacheKey)) return keyCache.get(cacheKey);
  if (!env.KICK_CLIENT_SECRET) throw new Error("server_not_configured");
  const base = await crypto.subtle.importKey("raw", enc.encode(env.KICK_CLIENT_SECRET), "HKDF", false, ["deriveKey"]);
  const algo = usage === "sign" ? { name: "HMAC", hash: "SHA-256", length: 256 } : { name: "AES-GCM", length: 256 };
  const key = await crypto.subtle.deriveKey(
    { name: "HKDF", hash: "SHA-256", salt: enc.encode("catego-web"), info: enc.encode(purpose) },
    base, algo, false, usage === "sign" ? ["sign", "verify"] : ["encrypt", "decrypt"]);
  keyCache.set(cacheKey, key);
  return key;
}

/** עוגיית התחברות: userId.expires.signature */
export async function signSession(env, userId, days = 30) {
  const exp = Date.now() + days * 86400_000;
  const payload = `${userId}.${exp}`;
  const sig = await crypto.subtle.sign("HMAC", await derive(env, "session", "sign"), enc.encode(payload));
  return `${payload}.${b64url(sig)}`;
}

export async function verifySession(env, value) {
  if (!value) return null;
  const parts = value.split(".");
  if (parts.length !== 3) return null;
  const [uid, exp, sig] = parts;
  if (!/^\d+$/.test(uid) || !(Number(exp) > Date.now())) return null;
  try {
    const ok = await crypto.subtle.verify("HMAC", await derive(env, "session", "sign"), fromB64url(sig), enc.encode(`${uid}.${exp}`));
    return ok ? Number(uid) : null;
  } catch {
    return null;
  }
}

/** הצפנת הטוקנים של קיק לפני שמירה. */
export async function seal(env, text) {
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const data = await crypto.subtle.encrypt({ name: "AES-GCM", iv }, await derive(env, "tokens", "encrypt"), enc.encode(text));
  return b64url(iv) + "." + b64url(data);
}

export async function unseal(env, sealed) {
  const [iv, data] = String(sealed).split(".");
  const plain = await crypto.subtle.decrypt({ name: "AES-GCM", iv: fromB64url(iv) }, await derive(env, "tokens", "encrypt"), fromB64url(data));
  return new TextDecoder().decode(plain);
}

export async function pkce() {
  const verifier = randomId(48);
  const challenge = b64url(await crypto.subtle.digest("SHA-256", enc.encode(verifier)));
  return { verifier, challenge };
}

export function cookies(request) {
  const out = {};
  for (const part of (request.headers.get("Cookie") || "").split(";")) {
    const i = part.indexOf("=");
    if (i > 0) out[part.slice(0, i).trim()] = decodeURIComponent(part.slice(i + 1).trim());
  }
  return out;
}
