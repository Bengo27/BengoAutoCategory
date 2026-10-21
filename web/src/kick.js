// קריאות ל-API הרשמי של Kick (OAuth + ערוץ + פרסי נקודות + אירועים)
export const KICK = {
  authorize: "https://id.kick.com/oauth/authorize",
  token: "https://id.kick.com/oauth/token",
  revoke: "https://id.kick.com/oauth/revoke",
  api: "https://api.kick.com/public/v1",
};

export const SCOPES = "user:read channel:read channel:rewards:read channel:rewards:write events:subscribe";
export const REDEMPTION_EVENT = "channel.reward.redemption.updated";
export const CHAT_EVENT = "chat.message.sent";

export class KickError extends Error {
  constructor(message, status) {
    super(message);
    this.status = status;
  }
}

async function tokenRequest(env, params) {
  const body = new URLSearchParams({ ...params, client_id: env.KICK_CLIENT_ID, client_secret: env.KICK_CLIENT_SECRET });
  const res = await fetch(KICK.token, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded", Accept: "application/json" },
    body: body.toString(),
  });
  const text = await res.text();
  if (!res.ok) throw new KickError("קיק דחה את ההתחברות", res.status);
  let j;
  try { j = JSON.parse(text); } catch { throw new KickError("תשובה לא תקינה מקיק", 502); }
  if (j.data && typeof j.data === "object") j = j.data;
  if (!j.access_token) throw new KickError("קיק לא החזיר טוקן", 502);
  return {
    access: j.access_token,
    refresh: j.refresh_token || "",
    expires: Date.now() + (Number(j.expires_in) || 3600) * 1000,
    scope: j.scope || "",
  };
}

export const exchangeCode = (env, code, verifier, redirect) =>
  tokenRequest(env, { grant_type: "authorization_code", code, code_verifier: verifier, redirect_uri: redirect });

export const refreshTokens = (env, refresh) => tokenRequest(env, { grant_type: "refresh_token", refresh_token: refresh });

/** קריאה מאושרת ל-API. מחזיר JSON (או null ל-204). */
export async function kickApi(token, method, path, body) {
  const res = await fetch(KICK.api + path, {
    method,
    headers: {
      Authorization: "Bearer " + token,
      Accept: "application/json",
      ...(body ? { "Content-Type": "application/json" } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (res.status === 204) return null;
  const text = await res.text();
  if (!res.ok) {
    const msg =
      res.status === 401 ? "החיבור לקיק פג – התחבר מחדש."
      : res.status === 403 ? "לקיק אין הרשאה לפעולה הזו – התנתק והתחבר מחדש."
      : res.status === 404 ? "לא נמצא בקיק."
      : res.status === 429 ? "יותר מדי בקשות לקיק – ננסה שוב בעוד רגע."
      : `קיק החזיר שגיאה ${res.status}`;
    throw new KickError(msg, res.status);
  }
  try { return text ? JSON.parse(text) : null; } catch { return null; }
}

const first = (j) => (Array.isArray(j?.data) ? j.data[0] : j?.data) || null;

export async function getMe(token) {
  const u = first(await kickApi(token, "GET", "/users"));
  if (!u) throw new KickError("קיק לא החזיר פרטי משתמש", 502);
  let slug = "";
  try { slug = first(await kickApi(token, "GET", "/channels"))?.slug || ""; } catch {}
  return { id: Number(u.user_id), name: u.name || slug || "streamer", slug };
}

export const parseReward = (r) =>
  r && r.id ? {
    id: r.id, title: r.title || "", cost: Number(r.cost) || 0,
    enabled: r.is_enabled !== false, paused: !!r.is_paused, input: !!r.is_user_input_required,
  } : null;

export async function createReward(token, { title, cost, description, color }) {
  const j = await kickApi(token, "POST", "/channels/rewards", {
    title, cost, description, background_color: color, is_enabled: true,
    is_user_input_required: true, should_redemptions_skip_request_queue: false,
  });
  return parseReward(first(j));
}

export async function updateReward(token, id, changes) {
  return parseReward(first(await kickApi(token, "PATCH", "/channels/rewards/" + encodeURIComponent(id), changes)));
}

/** בקשות שממתינות לפרס (עד 5 עמודים). */
export async function pendingRedemptions(token, rewardId) {
  const out = [];
  let cursor = "";
  for (let page = 0; page < 5; page++) {
    let path = `/channels/rewards/redemptions?reward_id=${encodeURIComponent(rewardId)}&status=pending`;
    if (cursor) path += "&cursor=" + encodeURIComponent(cursor);
    const j = await kickApi(token, "GET", path);
    for (const group of j?.data || []) {
      for (const r of group.redemptions || []) {
        if (r.status && r.status !== "pending") continue;
        out.push({
          id: r.id, userId: Number(r.redeemer?.user_id) || 0, username: r.redeemer?.username || "",
          input: r.user_input || "", at: Date.parse(r.redeemed_at) || Date.now(),
        });
      }
    }
    cursor = j?.pagination?.next_cursor || "";
    if (!cursor) break;
  }
  return out;
}

export async function resolveRedemptions(token, action, ids) {
  for (let i = 0; i < ids.length; i += 25) {
    await kickApi(token, "POST", "/channels/rewards/redemptions/" + action, { ids: ids.slice(i, i + 25) });
  }
}

export async function userNames(token, ids) {
  const map = {};
  const uniq = [...new Set(ids.filter((x) => x > 0))];
  for (let i = 0; i < uniq.length; i += 50) {
    const q = uniq.slice(i, i + 50).map((id) => "id=" + id).join("&");
    const j = await kickApi(token, "GET", "/users?" + q);
    for (const u of j?.data || []) if (u.user_id && u.name) map[Number(u.user_id)] = u.name;
  }
  return map;
}

/** נרשם להתראות מקיק (Webhook) על האירועים האלה. לא נרשם פעמיים לאותו אירוע. */
export async function subscribeEvents(token, names) {
  const existing = await kickApi(token, "GET", "/events/subscriptions");
  const have = new Set((existing?.data || []).map((s) => s.event));
  const missing = names.filter((n) => !have.has(n));
  if (!missing.length) return true;
  const j = await kickApi(token, "POST", "/events/subscriptions", {
    events: missing.map((name) => ({ name, version: 1 })), method: "webhook",
  });
  const bad = (j?.data || []).find((i) => i?.error);
  if (bad) throw new KickError("ההרשמה להתראות נכשלה: " + bad.error, 400);
  return true;
}

export const subscribeRedemptions = (token) => subscribeEvents(token, [REDEMPTION_EVENT]);

/** מבטל הרשמה לאירוע (למשל הצ'אט, כשמכבים הקראה מהצ'אט – חוסך בקשות). */
export async function unsubscribeEvent(token, name) {
  const existing = await kickApi(token, "GET", "/events/subscriptions");
  const ids = (existing?.data || []).filter((s) => s.event === name).map((s) => s.id).filter(Boolean);
  if (!ids.length) return;
  await kickApi(token, "DELETE", "/events/subscriptions?" + ids.map((id) => "id=" + encodeURIComponent(id)).join("&"));
}

/** תגי המשתמש בצ'אט (broadcaster / moderator / vip / subscriber / og / founder …). */
export function badgeTypes(sender) {
  return (sender?.identity?.badges || []).map((b) => String(b?.type || "").toLowerCase()).filter(Boolean);
}

// ───────── אימות Webhook (RSA PKCS#1 v1.5 + SHA-256, לפי התיעוד של Kick) ─────────
export const KICK_PUBLIC_KEY = `-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAq/+l1WnlRrGSolDMA+A8
6rAhMbQGmQ2SapVcGM3zq8ANXjnhDWocMqfWcTd95btDydITa10kDvHzw9WQOqp2
MZI7ZyrfzJuz5nhTPCiJwTwnEtWft7nV14BYRDHvlfqPUaZ+1KR4OCaO/wWIk/rQ
L/TjY0M70gse8rlBkbo2a8rKhu69RQTRsoaf4DVhDPEeSeI5jVrRDGAMGL3cGuyY
6CLKGdjVEM78g3JfYOvDU/RvfqD7L89TZ3iN94jrmWdGz34JNlEI5hqK8dd7C5EF
BEbZ5jgB8s8ReQV8H+MkuffjdAj3ajDDX3DOJMIut1lBrUVD1AaSrGCKHooWoL2e
twIDAQAB
-----END PUBLIC KEY-----`;

let cachedKey = null;
async function importKey(pem) {
  const b64 = pem.replace(/-----[^-]+-----/g, "").replace(/\s+/g, "");
  const der = Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));
  return crypto.subtle.importKey("spki", der, { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" }, false, ["verify"]);
}

export async function verifyWebhook(headers, rawBody, pem = KICK_PUBLIC_KEY) {
  const id = headers.get("Kick-Event-Message-Id");
  const ts = headers.get("Kick-Event-Message-Timestamp");
  const sig = headers.get("Kick-Event-Signature");
  if (!id || !ts || !sig) return false;
  try {
    const key = pem === KICK_PUBLIC_KEY ? (cachedKey ??= await importKey(pem)) : await importKey(pem);
    const signature = Uint8Array.from(atob(sig), (c) => c.charCodeAt(0));
    return await crypto.subtle.verify("RSASSA-PKCS1-v1_5", key, signature, new TextEncoder().encode(`${id}.${ts}.${rawBody}`));
  } catch {
    return false;
  }
}
