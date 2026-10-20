// Catego TTS – האתר. נתיבים:
//   /                 דף הבית ("התחבר עם קיק")
//   /auth/*           התחברות לקיק (OAuth + PKCE)
//   /app              לוח הבקרה של הסטרימר
//   /api/*            פעולות מלוח הבקרה (דורש התחברות)
//   /o/<id>-<key>     הווידג'ט ל-OBS (קישור אישי, בלי התחברות)
//   /kick/webhook     התראות מקיק על קניית פרסים
import { ChannelDO } from "./channel.js";
import { KICK, SCOPES, exchangeCode, getMe, verifyWebhook, REDEMPTION_EVENT } from "./kick.js";
import { signSession, verifySession, pkce, randomId, cookies } from "./crypto.js";
import { landingPage, dashboardPage, overlayPage } from "./pages.js";

export { ChannelDO };

const SECURITY_HEADERS = {
  "X-Content-Type-Options": "nosniff",
  "Referrer-Policy": "no-referrer",
  "X-Frame-Options": "DENY",
};

const json = (data, status = 200) =>
  new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json", "Cache-Control": "no-store", ...SECURITY_HEADERS } });
const html = (body, extra = {}) =>
  new Response(body, { headers: { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store", ...SECURITY_HEADERS, ...extra } });
const redirect = (to, headers = {}) => new Response(null, { status: 302, headers: { Location: to, ...headers } });

const cookie = (name, value, maxAge, path = "/") =>
  `${name}=${encodeURIComponent(value)}; Path=${path}; Max-Age=${maxAge}; HttpOnly; Secure; SameSite=Lax`;

const channel = (env, userId) => env.CHANNEL.get(env.CHANNEL.idFromName(String(userId)));

/** קריאה לאובייקט של הסטרימר. */
function call(env, userId, op, { method = "GET", body, query = "", request } = {}) {
  const url = `https://channel/${op}${query}`;
  if (request) return channel(env, userId).fetch(new Request(url, request));
  return channel(env, userId).fetch(url, {
    method,
    headers: body ? { "Content-Type": "application/json" } : {},
    body: body ? JSON.stringify(body) : undefined,
  });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const path = url.pathname;
    try {
      if (path === "/health") {
        return json({
          ok: true, service: "catego-web",
          kick: !!(env.KICK_CLIENT_ID && env.KICK_CLIENT_SECRET),
          tts: !!(env.AZURE_SPEECH_KEY && env.AZURE_SPEECH_REGION),
        });
      }
      if (path === "/kick/webhook" && request.method === "POST") return webhook(request, env);
      if (path.startsWith("/o/")) return overlay(request, env, url);
      if (path.startsWith("/auth/")) return auth(request, env, url);

      const uid = await verifySession(env, cookies(request).catego_session).catch(() => null);

      if (path === "/") return uid ? redirect("/app") : html(landingPage({ error: url.searchParams.get("error") }));
      if (path === "/app") return uid ? html(dashboardPage()) : redirect("/");
      if (path.startsWith("/api/")) {
        if (!uid) return json({ error: "לא מחובר" }, 401);
        return api(request, env, url, uid);
      }
      return json({ error: "not_found" }, 404);
    } catch (e) {
      return json({ error: e?.message === "server_not_configured" ? "האתר עוד לא הוגדר (חסרים משתנים ב-Cloudflare)." : "שגיאה בשרת" }, 500);
    }
  },
};

// ───────────────────────── התחברות ─────────────────────────

async function auth(request, env, url) {
  const redirectUri = url.origin + "/auth/callback";

  if (url.pathname === "/auth/login") {
    if (!env.KICK_CLIENT_ID || !env.KICK_CLIENT_SECRET) return redirect("/?error=config");
    const { verifier, challenge } = await pkce();
    const state = randomId(18);
    const q = new URLSearchParams({
      response_type: "code", client_id: env.KICK_CLIENT_ID, redirect_uri: redirectUri, scope: SCOPES,
      code_challenge: challenge, code_challenge_method: "S256", state,
    });
    return redirect(`${KICK.authorize}?${q}`, { "Set-Cookie": cookie("catego_oauth", `${state}.${verifier}`, 600, "/auth") });
  }

  if (url.pathname === "/auth/callback") {
    const saved = cookies(request).catego_oauth || "";
    const [state, verifier] = saved.split(".");
    const clear = cookie("catego_oauth", "", 0, "/auth");
    if (url.searchParams.get("error")) return redirect("/?error=denied", { "Set-Cookie": clear });
    if (!state || state !== url.searchParams.get("state") || !url.searchParams.get("code")) {
      return redirect("/?error=state", { "Set-Cookie": clear });
    }
    let tokens, user;
    try {
      tokens = await exchangeCode(env, url.searchParams.get("code"), verifier, redirectUri);
      user = await getMe(tokens.access);
    } catch {
      return redirect("/?error=kick", { "Set-Cookie": clear });
    }
    await call(env, user.id, "init", { method: "POST", body: { user, tokens } });
    const session = await signSession(env, user.id);
    const headers = new Headers({ Location: "/app" });
    headers.append("Set-Cookie", clear);
    headers.append("Set-Cookie", cookie("catego_session", session, 30 * 86400));
    return new Response(null, { status: 302, headers });
  }

  if (url.pathname === "/auth/logout" && request.method === "POST") {
    if (!sameOrigin(request, url)) return json({ error: "forbidden" }, 403);
    const uid = await verifySession(env, cookies(request).catego_session).catch(() => null);
    if (uid) await call(env, uid, "logout", { method: "POST", body: {} });
    return redirect("/", { "Set-Cookie": cookie("catego_session", "", 0) });
  }

  return json({ error: "not_found" }, 404);
}

const sameOrigin = (request, url) => request.headers.get("Origin") === url.origin;

// ───────────────────────── לוח הבקרה ─────────────────────────

const POST_OPS = new Set(["settings", "enable", "reward", "skip", "pause", "approve", "reject", "test", "rotate", "voice-test"]);

async function api(request, env, url, uid) {
  const op = url.pathname.slice("/api/".length);
  if (op === "ws") return call(env, uid, "ws", { query: url.searchParams.get("speaker") === "1" ? "?role=speaker" : "?role=dashboard", request });
  if (op === "state" && request.method === "GET") return passthrough(await call(env, uid, "state"));
  if (request.method === "POST" && POST_OPS.has(op)) {
    if (!sameOrigin(request, url)) return json({ error: "forbidden" }, 403);
    const raw = await request.text();
    if (raw.length > 32_000) return json({ error: "too_large" }, 413);
    let body = {};
    try { body = raw ? JSON.parse(raw) : {}; } catch { return json({ error: "invalid_json" }, 400); }
    return passthrough(await call(env, uid, op, { method: "POST", body }));
  }
  return json({ error: "not_found" }, 404);
}

function passthrough(res) {
  const headers = new Headers(res.headers);
  for (const [k, v] of Object.entries(SECURITY_HEADERS)) headers.set(k, v);
  return new Response(res.body, { status: res.status, headers });
}

// ───────────────────────── הווידג'ט ל-OBS ─────────────────────────

async function overlay(request, env, url) {
  // /o/<userId>-<key>[/ws|/audio]
  const m = url.pathname.match(/^\/o\/(\d+)-([A-Za-z0-9_-]{10,64})(\/ws|\/audio)?$/);
  if (!m) return json({ error: "not_found" }, 404);
  const [, uid, key, sub] = m;
  const q = `?key=${encodeURIComponent(key)}`;
  if (sub === "/ws") return call(env, uid, "ws", { query: q + "&role=overlay", request });
  if (sub === "/audio") return passthrough(await call(env, uid, "audio", { query: q + "&id=" + encodeURIComponent(url.searchParams.get("id") || "") }));
  const check = await call(env, uid, "overlay", { query: q });
  if (check.status !== 200) return html("<!doctype html><meta charset=utf-8><body style='background:transparent'></body>", {});
  // הווידג'ט נטען בתוך OBS – בלי X-Frame-Options כדי שיהיה אפשר להציג תצוגה מקדימה בלוח הבקרה
  return new Response(overlayPage(), { headers: { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff", "Referrer-Policy": "no-referrer" } });
}

// ───────────────────────── Webhook מקיק ─────────────────────────

async function webhook(request, env) {
  const raw = await request.text();
  if (raw.length > 64_000) return json({ error: "too_large" }, 413);
  if (!(await verifyWebhook(request.headers, raw))) return json({ error: "bad_signature" }, 401);
  if (request.headers.get("Kick-Event-Type") !== REDEMPTION_EVENT) return json({ ok: true });
  let p;
  try { p = JSON.parse(raw); } catch { return json({ error: "invalid_json" }, 400); }
  const broadcaster = Number(p?.broadcaster?.user_id);
  if (!broadcaster || !p?.id) return json({ ok: true });
  await call(env, broadcaster, "webhook", {
    method: "POST",
    body: {
      id: String(p.id), rewardId: p.reward?.id || null, status: p.status || "pending",
      userId: Number(p.redeemer?.user_id) || 0, username: p.redeemer?.username || "",
      input: p.user_input || "", at: Date.parse(p.redeemed_at) || Date.now(),
    },
  });
  return json({ ok: true });
}
