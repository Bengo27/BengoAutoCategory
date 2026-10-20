// בדיקות לאתר (Node 20+): node web/test/test.mjs
import worker, { ChannelDO } from "../src/index.js";
import { checkMessage, defaultSettings, sanitizeSettings, containsWord, applyTemplate } from "../src/tts.js";
import { signSession, verifySession, seal, unseal } from "../src/crypto.js";
import { verifyWebhook } from "../src/kick.js";

let failed = 0, passed = 0;
const check = (cond, name) => { if (cond) passed++; else { failed++; console.log("FAIL  " + name); } };
const test = async (name, fn) => { try { await fn(); console.log("PASS  " + name); } catch (e) { failed++; console.log("FAIL  " + name + ": " + e.stack); } };

// ───────── קיק + Azure מדומים ─────────
class FakeKick {
  constructor() { this.reset(); }
  reset() {
    this.calls = []; this.rewards = []; this.pending = []; this.accepted = []; this.rejected = [];
    this.subscriptions = []; this.access = "acc-1"; this.refresh = "ref-1"; this.counter = 1; this.azureFail = false;
  }
  async fetch(input, init = {}) {
    const url = typeof input === "string" ? input : input.url;
    const method = init.method || "GET";
    const body = init.body ? String(init.body) : "";
    this.calls.push({ url, method, body });
    const J = (s, d) => new Response(JSON.stringify(d), { status: s, headers: { "Content-Type": "application/json" } });
    if (url.includes("tts.speech.microsoft.com")) {
      if (this.azureFail) return new Response("", { status: 500 });
      return new Response(new Uint8Array(12000), { status: 200, headers: { "Content-Type": "audio/mpeg" } });
    }
    if (url.startsWith("https://id.kick.com/oauth/token")) {
      const f = new URLSearchParams(body);
      if (f.get("client_secret") !== "secret") return J(401, { error: "invalid_client" });
      if (f.get("grant_type") === "refresh_token" && f.get("refresh_token") !== this.refresh) return J(400, { error: "invalid_grant" });
      if (f.get("grant_type") === "authorization_code" && f.get("code") !== "good-code") return J(400, { error: "bad" });
      this.counter++; this.access = "acc-" + this.counter; this.refresh = "ref-" + this.counter;
      return J(200, { access_token: this.access, refresh_token: this.refresh, expires_in: 3600, scope: "user:read channel:read channel:rewards:read channel:rewards:write events:subscribe" });
    }
    if (url.startsWith("https://id.kick.com/oauth/revoke")) return new Response("", { status: 200 });
    const auth = new Headers(init.headers || {}).get("Authorization");
    if (auth !== "Bearer " + this.access) return J(401, { message: "Unauthorized" });
    const path = url.replace("https://api.kick.com/public/v1", "");
    if (path.startsWith("/users?")) {
      const ids = [...new URL(url).searchParams.getAll("id")];
      return J(200, { data: ids.map((id) => ({ user_id: Number(id), name: "viewer" + id })) });
    }
    if (path === "/users") return J(200, { data: [{ user_id: 123, name: "Bengo23" }] });
    if (path === "/channels") return J(200, { data: [{ slug: "bengo23", broadcaster_user_id: 123 }] });
    if (path.startsWith("/channels/rewards/redemptions/")) {
      const ids = JSON.parse(body).ids;
      (path.endsWith("/accept") ? this.accepted : this.rejected).push(...ids);
      this.pending = this.pending.filter((p) => !ids.includes(p.id));
      return J(200, { data: [] });
    }
    if (path.startsWith("/channels/rewards/redemptions")) {
      const rid = new URL(url).searchParams.get("reward_id");
      const reds = this.pending.filter((p) => p.rewardId === rid).map((p) => ({
        id: p.id, redeemed_at: new Date(p.at).toISOString(), redeemer: { user_id: p.userId }, status: "pending", user_input: p.input,
      }));
      return J(200, { data: [{ reward: { id: rid }, redemptions: reds }], pagination: { next_cursor: "" } });
    }
    if (path === "/channels/rewards" && method === "POST") {
      const b = JSON.parse(body);
      const r = { id: "01REWARD" + (this.rewards.length + 1), title: b.title, cost: b.cost, is_paused: false, is_user_input_required: b.is_user_input_required };
      this.rewards.push(r);
      return J(200, { data: r });
    }
    if (path.startsWith("/channels/rewards/") && method === "PATCH") {
      const r = this.rewards.find((x) => x.id === path.split("/")[3]);
      if (!r) return J(404, { message: "not found" });
      Object.assign(r, JSON.parse(body));
      return J(200, { data: r });
    }
    if (path === "/events/subscriptions" && method === "GET") return J(200, { data: this.subscriptions });
    if (path === "/events/subscriptions" && method === "POST") {
      this.subscriptions.push({ event: JSON.parse(body).events[0].name });
      return J(200, { data: [{ name: "channel.reward.redemption.updated", version: 1, subscription_id: "s1", error: null }] });
    }
    return J(404, { message: "nope " + path });
  }
}
const kick = new FakeKick();
globalThis.fetch = (i, init) => kick.fetch(i, init);

// ───────── Durable Object מדומה ─────────
class FakeSocket {
  constructor() { this.sent = []; this.readyState = 1; }
  send(t) { this.sent.push(JSON.parse(t)); }
  close() { this.readyState = 3; }
  last(type) { return [...this.sent].reverse().find((m) => m.type === type); }
}
class FakeState {
  constructor() { this.map = new Map(); this.sockets = []; this.alarm = null; this.waits = [];
    this.storage = {
      get: async (k) => structuredClone(this.map.get(k)), put: async (k, v) => { this.map.set(k, structuredClone(v)); },
      setAlarm: async (t) => { this.alarm = t; }, deleteAlarm: async () => { this.alarm = null; },
    };
  }
  blockConcurrencyWhile(fn) { this.ready = fn(); }
  getWebSockets(role) { return this.sockets.filter((s) => s.role === role).map((s) => s.ws); }
  addSocket(role) { const ws = new FakeSocket(); this.sockets.push({ role, ws }); return ws; }
  waitUntil(p) { this.waits.push(p); }
}

const env = { KICK_CLIENT_ID: "cid", KICK_CLIENT_SECRET: "secret", AZURE_SPEECH_KEY: "az", AZURE_SPEECH_REGION: "westeurope" };
const objects = new Map();
env.CHANNEL = {
  idFromName: (n) => n,
  get: (id) => {
    if (!objects.has(id)) { const state = new FakeState(); objects.set(id, { state, obj: new ChannelDO(state, env) }); }
    const o = objects.get(id);
    return { fetch: async (u, init) => { await o.state.ready; return o.obj.fetch(u instanceof Request ? u : new Request(u, init)); } };
  },
};

async function freshChannel(playback = "server") {
  kick.reset();
  objects.clear();
  const stub = env.CHANNEL.get("123");
  const o = objects.get("123");
  const call = async (op, body) => {
    const r = await stub.fetch("https://channel/" + op, body === undefined ? {} : { method: "POST", body: JSON.stringify(body), headers: { "Content-Type": "application/json" } });
    return { status: r.status, json: r.headers.get("Content-Type")?.includes("json") ? await r.json() : null, res: r };
  };
  await call("init", { user: { id: 123, name: "Bengo23", slug: "bengo23" },
    tokens: { access: kick.access, refresh: kick.refresh, expires: Date.now() + 3600_000, scope: "user:read channel:read channel:rewards:read channel:rewards:write events:subscribe" } });
  await call("settings", { settings: { playback } });
  return { o, call, d: () => o.obj.data };
}
const redeem = (id, userId, input, username = "", minutesAgo = 0, rewardId = "01REWARD1") =>
  ({ id, rewardId, status: "pending", userId, username, input, at: Date.now() - minutesAgo * 60_000 });

// ───────────────────────── בדיקות ─────────────────────────

await test("filter: links, banned words, cleanup", () => {
  const s = defaultSettings();
  check(!checkMessage("תיכנסו ל-https://scam.example", s).ok, "http link");
  check(!checkMessage("bit.ly/abc", s).ok, "short link");
  check(!checkMessage("אתה והזונה שלך", s).ok, "Hebrew prefix");
  check(!checkMessage("FUCK this", s).ok, "case");
  check(checkMessage("shitake mushrooms", s).ok, "substring ok");
  check(!checkMessage("[emote:1:KEKW]", s).ok, "emote only");
  check(checkMessage("חחחחחחח [emote:3:x] איזה   משחק", s).text === "חחח איזה משחק", "cleanup");
  check(checkMessage("12345678901234", { ...s, maxChars: 10 }).text === "1234567890…", "truncate");
  check(containsWord("ככה זה כוסאמק", "כוסאמק"), "containsWord");
  check(applyTemplate("{user} אומר: {text}", "דני", "היי") === "דני אומר: היי", "template");
  const t = sanitizeSettings({ rewardCost: 0, voice: "x", maxChars: 9999, template: "no", rate: 99, banned: [" a ", "a", ""] });
  check(t.rewardCost === 1 && t.voice === "he-IL-HilaNeural" && t.maxChars === 400 && t.template.includes("{text}") && t.rate === 50 && t.banned.length === 1, "sanitize");
});

await test("crypto: sessions + sealed tokens", async () => {
  const s = await signSession(env, 123);
  check((await verifySession(env, s)) === 123, "valid session");
  check((await verifySession(env, s.replace(/^123/, "124"))) === null, "tampered user id rejected");
  check((await verifySession({ ...env, KICK_CLIENT_SECRET: "other" }, s)) === null, "other key rejected");
  check((await verifySession(env, "garbage")) === null, "garbage");
  const sealed = await seal(env, "token-xyz");
  check(!sealed.includes("token-xyz") && (await unseal(env, sealed)) === "token-xyz", "seal/unseal");
});

await test("webhook signature (RSA PKCS1 v1.5 SHA-256)", async () => {
  const kp = await crypto.subtle.generateKey({ name: "RSASSA-PKCS1-v1_5", modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]), hash: "SHA-256" }, true, ["sign", "verify"]);
  const spki = new Uint8Array(await crypto.subtle.exportKey("spki", kp.publicKey));
  const pem = "-----BEGIN PUBLIC KEY-----\n" + btoa(String.fromCharCode(...spki)) + "\n-----END PUBLIC KEY-----";
  const body = '{"id":"x"}';
  const sig = new Uint8Array(await crypto.subtle.sign("RSASSA-PKCS1-v1_5", kp.privateKey, new TextEncoder().encode(`m1.2026-10-20T10:00:00Z.${body}`)));
  const headers = new Headers({ "Kick-Event-Message-Id": "m1", "Kick-Event-Message-Timestamp": "2026-10-20T10:00:00Z", "Kick-Event-Signature": btoa(String.fromCharCode(...sig)) });
  check(await verifyWebhook(headers, body, pem), "valid signature");
  check(!(await verifyWebhook(headers, body + " ", pem)), "changed body rejected");
  check(!(await verifyWebhook(new Headers(), body, pem)), "missing headers");
});

await test("reward: create, subscribe to webhook, update, recreate, pause", async () => {
  const { call, d } = await freshChannel();
  let r = await call("enable", { enabled: true });
  check(r.status === 200, "enable ok " + JSON.stringify(r.json));
  check(kick.rewards.length === 1 && d().rewardId === "01REWARD1", "reward created on first enable");
  check(kick.rewards[0].is_user_input_required === true, "user input required");
  check(d().webhook === true && kick.subscriptions.length === 1, "subscribed to redemption webhook");
  await call("settings", { settings: { rewardCost: 900 } });
  check(kick.rewards[0].cost === 900 && kick.rewards.length === 1, "cost change updates reward");
  kick.rewards.length = 0;
  await call("reward", {});
  check(kick.rewards.length === 1 && d().rewardId === "01REWARD1", "recreated after deleted in Kick");
  await call("enable", { enabled: false });
  check(kick.rewards[0].is_paused === true, "disable pauses reward");
  await call("reward", {});
  check(kick.subscriptions.length === 1, "no duplicate subscription");
});

await test("webhook redemption → OBS plays → accepted", async () => {
  const { o, call, d } = await freshChannel();
  await call("enable", { enabled: true });
  const ws = o.state.addSocket("overlay");
  await call("webhook", redeem("R1", 42, "יאללה עוד סיבוב", "dani"));
  const play = ws.last("play");
  check(play && play.id === "R1" && play.user === "dani" && play.text === "יאללה עוד סיבוב", "overlay got play");
  const azure = kick.calls.find((c) => c.url.includes("tts.speech"));
  check(azure && azure.body.includes("dani אומר: יאללה עוד סיבוב") && azure.body.includes("he-IL-HilaNeural"), "spoken text + voice");
  const audio = await call("audio?key=" + d().overlayKey + "&id=R1");
  check(audio.status === 200 && audio.res.headers.get("Content-Type") === "audio/mpeg", "audio served");
  check((await call("audio?key=wrong&id=R1")).status === 403, "audio needs key");
  await o.obj.webSocketMessage(ws, JSON.stringify({ type: "done", id: "R1" }));
  check(kick.accepted.includes("R1") && d().items.find((i) => i.id === "R1").status === "done", "accepted after done");
  await call("webhook", redeem("R1", 42, "יאללה עוד סיבוב", "dani"));
  check(d().items.filter((i) => i.id === "R1").length === 1, "duplicate webhook ignored");
  await call("webhook", { ...redeem("OTHER", 1, "x"), rewardId: "SOME_OTHER_REWARD" });
  check(!d().items.some((i) => i.id === "OTHER"), "other rewards ignored");
});

await test("filters reject with refund; old/disabled too", async () => {
  const { o, call, d } = await freshChannel();
  await call("enable", { enabled: true });
  o.state.addSocket("overlay");
  await call("webhook", redeem("BAD", 1, "בן זונה"));
  await call("webhook", redeem("LINK", 2, "www.scam.com"));
  await call("webhook", redeem("OLD", 3, "היי", "", 30));
  for (const id of ["BAD", "LINK", "OLD"]) check(kick.rejected.includes(id), id + " refunded");
  check(!kick.calls.some((c) => c.url.includes("tts.speech")), "nothing spoken");
  await call("enable", { enabled: false });
  await call("webhook", redeem("OFF", 4, "היי"));
  check(kick.rejected.includes("OFF"), "refund while TTS is off");
});

await test("manual approval, reject, skip, pause", async () => {
  const { o, call, d } = await freshChannel();
  await call("settings", { settings: { manualApproval: true, gapSeconds: 0 } });
  await call("enable", { enabled: true });
  const ws = o.state.addSocket("overlay");
  await call("webhook", redeem("A", 1, "תאשר אותי", "a"));
  await call("webhook", redeem("B", 2, "ואותי לא", "b"));
  check(!ws.last("play"), "nothing before approval");
  await call("reject", { id: "B" });
  check(kick.rejected.includes("B"), "manual reject refunds");
  await call("approve", { id: "A" });
  check(ws.last("play")?.id === "A", "plays after approval");
  await call("skip", {});
  check(ws.last("stop")?.id === "A" && kick.accepted.includes("A") && d().items.find((i) => i.id === "A").status === "skipped", "skip stops + accepts");
  await call("settings", { settings: { manualApproval: false } });
  await call("pause", { paused: true });
  await call("webhook", redeem("C", 3, "היי", "c"));
  check(ws.last("play")?.id === "A", "paused queue holds");
  await call("pause", { paused: false });
  check(ws.last("play")?.id === "C", "resumes");
});

await test("no OBS open → waits; polling fallback; test message; failure refund", async () => {
  const { o, call, d } = await freshChannel();
  await call("enable", { enabled: true });
  await call("webhook", redeem("W", 1, "מחכה ל-OBS", "w"));
  check(d().items.find((i) => i.id === "W").status === "waiting" && !d().playing, "waits without overlay");
  // פספסנו Webhook: הבקשה קיימת רק בקיק
  kick.pending.push({ id: "P1", rewardId: "01REWARD1", userId: 77, input: "מהבדיקה התקופתית", at: Date.now() });
  const ws = o.state.addSocket("overlay");
  d().lastPoll = 0;
  await o.obj.alarm();
  check(d().items.some((i) => i.id === "P1" && i.user === "viewer77"), "poll found missed redemption + name");
  check(ws.last("play")?.id === "W", "queue starts when overlay opens");
  await o.obj.webSocketMessage(ws, JSON.stringify({ type: "done", id: "W" }));
  d().gapUntil = 0;
  await o.obj.alarm();
  check(ws.last("play")?.id === "P1", "next item");
  // הקראה שנתקעה (הווידג'ט לא דיווח) → מסתיימת לבד
  d().playing.deadline = Date.now() - 1;
  await o.obj.alarm();
  check(kick.accepted.includes("P1") && !d().playing, "stuck playback finishes by deadline");

  kick.azureFail = true;
  d().gapUntil = 0;
  await call("webhook", redeem("F", 5, "היי", "f"));
  check(kick.rejected.includes("F") && d().items.find((i) => i.id === "F").status === "failed", "voice failure refunds");
  kick.azureFail = false;

  await call("test", { text: "בדיקה" });
  const t = ws.last("play");
  check(t && t.user === "בדיקה", "test goes to OBS");
  await o.obj.webSocketMessage(ws, JSON.stringify({ type: "done", id: t.id }));
  check(!kick.accepted.includes(t.id), "test never touches Kick");
});

await test("Edge mode: tab speaks for free, OBS shows the card", async () => {
  const { o, call, d } = await freshChannel("edge");
  check(defaultSettings().playback === "edge", "Edge is the default");
  await call("enable", { enabled: true });
  const overlay = o.state.addSocket("overlay");
  await call("webhook", redeem("E1", 1, "שלום לכולם", "dani"));
  check(!overlay.last("play"), "no speaker tab → waits (overlay alone can't speak in Edge mode)");
  const tab = o.state.addSocket("speaker");
  await o.obj.alarm();
  const speak = tab.last("speak");
  check(speak && speak.id === "E1" && speak.text === "dani אומר: שלום לכולם" && speak.voice === "he-IL-HilaNeural", "tab got speak");
  check(overlay.last("play")?.silent === true && overlay.last("play").user === "dani", "overlay shows card silently");
  check(!kick.calls.some((c) => c.url.includes("tts.speech")), "Azure never called in Edge mode");
  await o.obj.webSocketMessage(tab, JSON.stringify({ type: "done", id: "E1" }));
  check(kick.accepted.includes("E1") && overlay.last("end")?.id === "E1", "accepted + overlay hides card");
  // לשונית בלי קול עברי → נכשל ומחזירים נקודות
  d().gapUntil = 0;
  await call("webhook", redeem("E2", 2, "היי", "x"));
  await o.obj.webSocketMessage(tab, JSON.stringify({ type: "done", id: "E2", error: "no-voice" }));
  check(kick.rejected.includes("E2") && d().items.find((i) => i.id === "E2").status === "failed", "no voice → refund");
  // דילוג עוצר גם את הלשונית
  d().gapUntil = 0;
  await call("webhook", redeem("E3", 3, "ארוך ארוך", "y"));
  await call("skip", {});
  check(tab.last("stop")?.id === "E3" && kick.accepted.includes("E3"), "skip stops the tab");
  check((await call("state")).json.speakers === 1, "state reports speaker tabs");
  // לשונית מקריאה חדשה מחליפה את הקודמת
  globalThis.WebSocketPair = class { constructor() { this[0] = new FakeSocket(); this[1] = new FakeSocket(); } };
  o.state.acceptWebSocket = (ws, tags) => o.state.sockets.push({ role: tags[0], ws });
  const req = new Request("https://channel/ws?role=speaker", { headers: { Upgrade: "websocket" } });
  try { o.obj.acceptSocket(req, new URL(req.url)); } catch {} // Node לא יודע ליצור תשובת 101
  check(tab.readyState === 3 && o.obj.sockets("speaker").length === 1, "new speaker tab replaces the old one");
});

await test("token refresh and expired login", async () => {
  const { call, d } = await freshChannel();
  d().auth.expires = Date.now() - 1;
  await call("reward", {});
  check(kick.rewards.length === 1, "worked after automatic refresh");
  kick.refresh = "changed-elsewhere";
  d().auth.expires = Date.now() - 1;
  const r = await call("reward", {});
  check(r.status === 400 && /התחבר/.test(r.json.error) && d().auth === null, "dead refresh → asks to log in again");
  check((await call("state")).json.connected === false, "state shows disconnected");
});

await test("voice test rate limit", async () => {
  const { call } = await freshChannel();
  let last;
  for (let i = 0; i < 11; i++) last = await call("voice-test", { text: "hi" });
  check(last.status === 429, "11th test in a minute is blocked");
});

await test("worker routes", async () => {
  objects.clear(); kick.reset();
  const W = (path, init) => worker.fetch(new Request("https://catego.test" + path, init), env);
  let r = await W("/health");
  let j = await r.json();
  check(j.ok && j.kick && j.tts, "health");
  check((await W("/api/state")).status === 401, "api needs login");
  check((await W("/app")).status === 302, "dashboard redirects when logged out");
  r = await W("/auth/login");
  const loc = new URL(r.headers.get("Location"));
  check(loc.origin === "https://id.kick.com" && loc.searchParams.get("scope").includes("channel:rewards:write") &&
    loc.searchParams.get("redirect_uri") === "https://catego.test/auth/callback" && loc.searchParams.get("code_challenge_method") === "S256", "login redirect");
  const oauth = r.headers.get("Set-Cookie").match(/catego_oauth=([^;]+)/)[1];
  const state = decodeURIComponent(oauth).split(".")[0];
  r = await W("/auth/callback?code=good-code&state=WRONG", { headers: { Cookie: "catego_oauth=" + oauth } });
  check(r.headers.get("Location") === "/?error=state", "state mismatch rejected");
  r = await W(`/auth/callback?code=good-code&state=${state}`, { headers: { Cookie: "catego_oauth=" + oauth } });
  check(r.headers.get("Location") === "/app", "callback logs in");
  const session = r.headers.getSetCookie().find((c) => c.startsWith("catego_session=")).match(/catego_session=([^;]+)/)[1];
  const C = { Cookie: "catego_session=" + session };
  j = await (await W("/api/state", { headers: C })).json();
  check(j.user?.name === "Bengo23" && j.connected, "logged-in state");
  check((await W("/api/enable", { method: "POST", headers: { ...C, "Content-Type": "application/json" }, body: '{"enabled":true}' })).status === 403, "POST without Origin blocked (CSRF)");
  r = await W("/api/enable", { method: "POST", headers: { ...C, Origin: "https://catego.test", "Content-Type": "application/json" }, body: '{"enabled":true}' });
  check(r.status === 200 && kick.rewards.length === 1, "enable through API");
  check((await W("/o/123-wrongkeywrongkey")).status === 200, "overlay with wrong key returns empty page");
  const overlayPath = j.overlayPath;
  const page = await (await W(overlayPath)).text();
  check(page.includes("/ws") && page.includes('dir="rtl"'), "overlay page");
  check((await W("/kick/webhook", { method: "POST", body: "{}" })).status === 401, "unsigned webhook rejected");
  check((await W("/nope")).status === 404, "404");
});

console.log(failed ? `\n${failed} FAILED (${passed} checks passed)` : `\nAll web tests passed (${passed} checks)`);
process.exit(failed ? 1 : 0);
