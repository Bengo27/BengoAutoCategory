// בדיקות לאתר (Node 20+): node web/test/test.mjs
import worker, { ChannelDO } from "../src/index.js";
import { pcmToWav, synthesizeGemini } from "../src/tts.js";
import { checkMessage, defaultSettings, sanitizeSettings, containsWord, applyTemplate, chatCommandText, chatAllowed, validPrefix } from "../src/tts.js";
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
    if (url.includes("generativelanguage.googleapis.com")) {
      if (this.geminiFail) return new Response('{"error":{"code":429}}', { status: 429 });
      const req = JSON.parse(body);
      this.geminiReq = { req, key: new Headers(init.headers).get("x-goog-api-key") };
      // PCM בלי כותרת – השרת צריך לעטוף ב-WAV
      const pcm = btoa(String.fromCharCode(...new Uint8Array(4800)));
      return J(200, { id: "int1", steps: [{ type: "model_output", content: [{ type: "audio", mime_type: "audio/l16", data: pcm }] }] });
    }
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
    if (url.startsWith("https://accounts.spotify.com/api/token")) {
      const f = new URLSearchParams(body);
      this.spotifyCalls = (this.spotifyCalls || 0) + 1;
      if (f.get("client_secret")) return J(400, { error: "secret_must_not_be_sent" });
      if (f.get("grant_type") === "authorization_code" && (f.get("code") !== "sp-code" || f.get("code_verifier") !== "ver")) return J(400, { error: "invalid_grant" });
      if (f.get("grant_type") === "refresh_token" && f.get("refresh_token") !== "sp-ref") return J(400, { error: "invalid_grant" });
      return J(200, { access_token: "sp-acc-" + this.spotifyCalls, refresh_token: "sp-ref", expires_in: 3600 });
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
    if (path.startsWith("/events/subscriptions?") && method === "DELETE") {
      const ids = new URL(url).searchParams.getAll("id");
      this.subscriptions = this.subscriptions.filter((x) => !ids.includes(x.id));
      return new Response(null, { status: 204 });
    }
    if (path === "/events/subscriptions" && method === "POST") {
      for (const e of JSON.parse(body).events) this.subscriptions.push({ id: "sub-" + e.name, event: e.name });
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

await test("chat command parsing + permissions", () => {
  check(chatCommandText("!tts שלום לכולם", "!tts") === "שלום לכולם", "basic");
  check(chatCommandText("!TTS hi", "!tts") === "hi", "case-insensitive");
  check(chatCommandText("!ttsx hi", "!tts") === null, "word prefix needs space");
  check(chatCommandText("!שלום", "!") === "שלום", "single-symbol prefix");
  check(chatCommandText("!tts", "!tts") === null && chatCommandText("hello", "!tts") === null, "empty / no prefix");
  check(validPrefix("!tts") && validPrefix("#") && !validPrefix("tts") && !validPrefix("! x"), "prefix must start with a symbol");
  check(sanitizeSettings({ chatPrefix: "tts" }).chatPrefix === "!tts", "invalid prefix rejected");
  check(chatAllowed("all", []) && !chatAllowed("subs", []) && chatAllowed("subs", ["subscriber"]), "subs");
  check(chatAllowed("vip", ["vip"]) && !chatAllowed("vip", ["subscriber"]) && chatAllowed("mods", ["broadcaster"]) && !chatAllowed("mods", ["vip"]), "vip/mods");
});

await test("chat TTS: subscribe, read, cooldown, permissions, no Kick calls", async () => {
  const { o, call, d } = await freshChannel("server");
  await call("enable", { enabled: true });
  await call("settings", { settings: { chatEnabled: true, chatCooldown: 60, gapSeconds: 0 } });
  check(kick.subscriptions.some((x) => x.event === "chat.message.sent") && d().chatSubscribed, "subscribed to chat events");
  const ws = o.state.addSocket("overlay");
  const msg = (id, userId, content, badges = []) => call("chat", { id, userId, username: "u" + userId, content, badges, at: Date.now() });
  await msg("m1", 5, "!tts אחי איזה משחק");
  check(ws.last("play")?.id === "chat-m1" && ws.last("play").text === "אחי איזה משחק", "chat message read aloud");
  await o.obj.webSocketMessage(ws, JSON.stringify({ type: "done", id: "chat-m1" }));
  check(!kick.accepted.includes("chat-m1") && !kick.calls.some((c) => c.url.includes("redemptions/") && c.body.includes("chat-")), "no accept/reject for chat");
  await msg("m2", 5, "!tts שוב אני");
  check(!d().items.some((i) => i.id === "chat-m2"), "cooldown per viewer");
  await msg("m3", 6, "!tts מוד בלי השהיה", ["moderator"]);
  await msg("m4", 6, "!tts מוד שוב", ["moderator"]);
  check(d().items.some((i) => i.id === "chat-m4"), "mods skip cooldown");
  await msg("m5", 7, "סתם הודעה בצ'אט");
  await msg("m6", 8, "!tts בן זונה");
  check(!d().items.some((i) => i.id === "chat-m5" || i.id === "chat-m6"), "non-commands and banned words ignored");
  await call("settings", { settings: { chatWho: "subs" } });
  await msg("m7", 9, "!tts אני לא סאב");
  await msg("m8", 10, "!tts אני סאב", ["subscriber"]);
  check(!d().items.some((i) => i.id === "chat-m7") && d().items.some((i) => i.id === "chat-m8"), "subs-only setting");
  check((await call("state")).json.items.some((i) => i.chat), "view marks chat items");
  await call("settings", { settings: { chatEnabled: false } });
  check(!kick.subscriptions.some((x) => x.event === "chat.message.sent"), "unsubscribed when turned off");
  await msg("m9", 11, "!tts כבוי");
  check(!d().items.some((i) => i.id === "chat-m9"), "ignored when chat TTS off");
});

await test("Gemini mode: free server voices, WAV to OBS, fallback to Azure", async () => {
  const wav = pcmToWav(new Uint8Array(10));
  check(String.fromCharCode(...wav.slice(0, 4)) === "RIFF" && wav.length === 54, "pcm wrapped as wav");
  const { o, call, d } = await freshChannel("gemini");
  const genv = { ...env, GEMINI_API_KEY: "g-key" };
  o.obj.env = genv;
  await call("settings", { settings: { geminiVoice: "Puck", gapSeconds: 0 } });
  await call("enable", { enabled: true });
  const ws = o.state.addSocket("overlay");
  await call("webhook", redeem("G1", 1, "שלום מג'מיני", "gal"));
  check(kick.geminiReq?.key === "g-key" && kick.geminiReq.req.generation_config.speech_config[0].voice === "Puck", "Gemini called with key + voice");
  check(kick.geminiReq.req.input[0].content[0].text === "gal אומר: שלום מג'מיני", "spoken text");
  check(ws.last("play")?.id === "G1" && !ws.last("play").silent, "overlay plays audio itself (no Edge tab needed)");
  const audio = await call("audio?key=" + d().overlayKey + "&id=G1");
  check(audio.res.headers.get("Content-Type") === "audio/wav", "served as wav");
  await o.obj.webSocketMessage(ws, JSON.stringify({ type: "done", id: "G1" }));
  // מגבלה יומית → Azure
  kick.geminiFail = true;
  d().gapUntil = 0;
  await call("webhook", redeem("G2", 2, "גיבוי", "b"));
  check(ws.last("play")?.id === "G2" && kick.calls.some((c) => c.url.includes("tts.speech.microsoft.com")), "falls back to Azure when Gemini is out of quota");
  await o.obj.webSocketMessage(ws, JSON.stringify({ type: "done", id: "G2" }));
  // בלי Azure → נכשל ומחזיר נקודות
  o.obj.env = { ...genv, AZURE_SPEECH_KEY: "" };
  d().gapUntil = 0;
  await call("webhook", redeem("G3", 3, "אין גיבוי", "c"));
  check(kick.rejected.includes("G3") && d().items.find((i) => i.id === "G3").note.includes("Gemini"), "no fallback → refund with Gemini message");
  check(/נכשלה/.test(d().error || ""), "failure is shown on the dashboard");
  // Gemini במגבלה + לשונית Edge פתוחה → מקריאים מהלשונית
  const tab = o.state.addSocket("speaker");
  d().gapUntil = 0;
  await call("test", { text: "בדיקה אחרי מגבלה" });
  check(tab.last("speak")?.text.includes("בדיקה אחרי מגבלה"), "falls back to the Edge tab when the server voice fails");
  check(d().items.some((i) => i.test && i.status === "playing"), "test item stays visible in the queue");
  kick.geminiFail = false;
  check((await call("state")).json.geminiTts === true, "state reports Gemini ready");
  let threw = false;
  try { await synthesizeGemini({}, "x", "Kore", 0); } catch (e) { threw = /מפתח/.test(e.message); }
  check(threw, "missing key → clear message");
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
  check(loc.searchParams.get("scope").includes("kicks:read"), "login asks for kicks:read");
  r = await W("/auth/logout", { method: "POST", headers: { ...C, Origin: "null", "Sec-Fetch-Site": "cross-site" } });
  check(r.status === 403, "cross-site logout blocked");
  r = await W("/auth/logout", { method: "POST", headers: { ...C, Origin: "null", "Sec-Fetch-Site": "same-origin" } });
  check(r.status === 302 && r.headers.get("Location") === "/" && /catego_session=;/.test(r.headers.get("Set-Cookie")), "logout form works (Origin: null)");
});

await test("alerts (kicks / follow / sub / gift)", async () => {
  const { alertText, defaultSettings: ds } = await import("../src/tts.js");
  const S = ds();
  check(alertText("kicks", S, { user: "דני", amount: 100, text: "יאללה" }) === "דני שלח 100 קיקס: יאללה", "kicks text");
  check(alertText("kicks", S, { user: "דני", amount: 50, text: "" }) === "דני שלח 50 קיקס!", "kicks without message");
  check(alertText("gift", S, { user: "המלך", count: 5 }).startsWith("המלך הביא 5 סאבים"), "gift text");
  const { o, call, d } = await freshChannel();
  const ws = o.state.addSocket("overlay");
  await call("enable", { enabled: true });
  // כבוי → לא מוקרא, אבל רואים למה
  await call("alert", { kind: "follow", id: "f1", user: "newguy" });
  check(!d().items.some((i) => i.alert) && /כבויה/.test(d().alertDebug.reason), "alert off → explained, not read");
  await call("settings", { settings: { alertFollow: true, alertKicks: true, alertKicksMin: 100 } });
  check(kick.subscriptions.some((x) => x.event === "channel.followed") && d().alertSubs.follow === true, "subscribed to follows");
  check(typeof d().alertSubs.kicks === "string" && /מחדש/.test(d().alertSubs.kicks), "kicks needs re-login (no kicks:read)");
  await call("alert", { kind: "follow", id: "f2", user: "newguy" });
  const it = d().items.find((i) => i.id === "alert-f2");
  check(it && it.spoken === "תודה על העוקב, newguy!", "follow queued with template");
  await o.obj.tick();
  const play = ws.last("play");
  const az = kick.calls.filter((c) => c.url.includes("tts.speech")).pop();
  check(play?.hidden === true, "alert: sound only by default (no TTS box over Botrix)");
  check(play?.id === "alert-f2" && az?.body.includes("תודה על העוקב, newguy!") && !az.body.includes("אומר:"), "alert spoken as-is (not with chat template)");
  await call("alert", { kind: "follow", id: "f2", user: "newguy" });
  check(d().items.filter((i) => i.id === "alert-f2").length === 1, "duplicate alert ignored");
  await call("alert", { kind: "kicks", id: "k1", user: "small", amount: 10, text: "hi" });
  check(!d().items.some((i) => i.id === "alert-k1") && /מינימלי/.test(d().alertDebug.reason), "kicks under minimum skipped");
  await call("alert", { kind: "kicks", id: "k2", user: "big", amount: 500, text: "FUCK this" });
  check(d().items.find((i) => i.id === "alert-k2")?.spoken === "big שלח 500 קיקס!", "blocked kicks message read without the message");
  await o.obj.webSocketMessage(ws, JSON.stringify({ type: "done", id: "alert-f2" }));
  check(!kick.accepted.includes("alert-f2") && !kick.rejected.includes("alert-f2"), "alerts never touch reward redemptions");
  const r = await call("alert-test", { kind: "gift" });
  check(r.status === 200 && d().items.some((i) => i.test && i.alert === "gift" && /5 סאבים/.test(i.spoken)), "test button for gifts");
  check((await call("alert-test", { kind: "nope" })).status === 400, "unknown test kind rejected");
  await call("settings", { settings: { alertFollow: false } });
  check(!kick.subscriptions.some((x) => x.event === "channel.followed"), "unsubscribed when turned off");
});

await test("alerts in Edge mode send nothing to OBS", async () => {
  const { o, call, d } = await freshChannel("edge");
  const overlay = o.state.addSocket("overlay"), tab = o.state.addSocket("speaker");
  await call("enable", { enabled: true });
  await call("alert-test", { kind: "follow" });
  check(tab.last("speak")?.text.includes("תודה על העוקב") && !overlay.last("play"), "tab speaks, overlay shows nothing");
  await o.obj.webSocketMessage(tab, JSON.stringify({ type: "done", id: tab.last("speak").id }));
  await call("settings", { settings: { alertShowCard: true } });
  d().gapUntil = 0;
  await call("alert-test", { kind: "sub" });
  check(overlay.last("play")?.silent === true, "with the toggle on the box shows");
});

await test("now playing (spotify)", async () => {
  const { sanitizeNp, validClientId } = await import("../src/nowplaying.js");
  check(sanitizeNp({ theme: "vinyl", accent: "#ff0000", opacity: 5, scale: 0.1 }).theme === "vinyl" && sanitizeNp({ opacity: 5 }).opacity === 1 && sanitizeNp({ scale: 0.1 }).scale === 0.6, "np sanitize clamps");
  check(sanitizeNp({ theme: "evil", accent: "red;x" }).theme === "classic" && sanitizeNp({ accent: "red;x" }).accent === "#a6ef67", "np sanitize rejects junk");
  check(validClientId("0123456789abcdef0123456789abcdef") && !validClientId("abc"), "client id format");
  const { o, call, d } = await freshChannel();
  check((await call("spotify-client", { clientId: "bad" })).status === 400, "bad client id rejected");
  const CID = "0123456789abcdef0123456789abcdef";
  check((await call("spotify-client", { clientId: CID })).status === 200 && d().np.clientId === CID, "client id saved");
  check((await call("spotify-tokens", { code: "wrong", verifier: "ver", redirect: "https://x/spotify/callback" })).status === 400 && !d().np.auth, "bad code rejected");
  const ws = o.state.addSocket("np");
  check((await call("spotify-tokens", { code: "sp-code", verifier: "ver", redirect: "https://x/spotify/callback" })).status === 200, "spotify connected");
  check(!JSON.stringify(d().np.auth).includes("sp-acc") && !JSON.stringify(d().np.auth).includes("sp-ref"), "spotify tokens sealed at rest");
  check(ws.last("token")?.access?.startsWith("sp-acc"), "widget got token on connect");
  let st = (await call("state")).json;
  check(st.np.connected && st.np.path === `/np/123-${d().overlayKey}` && !JSON.stringify(st).includes("sp-acc"), "state hides token");
  await o.obj.webSocketMessage(ws, JSON.stringify({ type: "token" }));
  check(ws.sent.filter((m) => m.type === "token").length === 2, "widget can ask for token");
  d().np.auth.expires = Date.now() - 1000;
  ws.lastTokenAsk = 0;
  const before = kick.spotifyCalls;
  await o.obj.webSocketMessage(ws, JSON.stringify({ type: "token" }));
  check(kick.spotifyCalls === before + 1 && ws.last("token").access === "sp-acc-" + (before + 1), "expired token refreshed");
  await call("np-settings", { settings: { theme: "neon" } });
  check(ws.last("config")?.settings.theme === "neon" && d().np.settings.theme === "neon", "settings pushed to widget");
  await call("spotify-disconnect", {});
  check(!d().np.auth && ws.last("token").access === null, "disconnect");
  await call("rotate", {});
  check(ws.readyState === 3, "rotating key closes np widget");
});

await test("now playing routes", async () => {
  objects.clear(); kick.reset();
  const W = (path, init) => worker.fetch(new Request("https://catego.test" + path, init), env);
  const stub = env.CHANNEL.get("123");
  await stub.fetch("https://channel/init", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ user: { id: 123, name: "Bengo23", slug: "bengo23" }, tokens: { access: kick.access, refresh: kick.refresh, expires: Date.now() + 3600_000 } }) });
  const session = await signSession(env, 123);
  const C = { Cookie: "catego_session=" + session };
  check((await W("/spotify/login")).headers.get("Location") === "/", "spotify login needs session");
  check((await W("/spotify/login", { headers: C })).headers.get("Location") === "/app#np", "no client id → back to dashboard");
  const P = (op, b) => W("/api/" + op, { method: "POST", headers: { ...C, Origin: "https://catego.test", "Content-Type": "application/json" }, body: JSON.stringify(b) });
  check((await P("spotify-client", { clientId: "0123456789abcdef0123456789abcdef" })).status === 200, "set client via API");
  check((await P("clear", {})).status === 200, "clear is an API op");
  const r = await W("/spotify/login", { headers: C });
  const loc = new URL(r.headers.get("Location"));
  check(loc.origin === "https://accounts.spotify.com" && loc.searchParams.get("redirect_uri") === "https://catego.test/spotify/callback" && loc.searchParams.get("code_challenge_method") === "S256" && loc.searchParams.get("scope") === "user-read-currently-playing", "spotify authorize redirect");
  const sp = r.headers.get("Set-Cookie").match(/catego_sp=([^;]+)/)[1];
  const state = decodeURIComponent(sp).split(".")[0];
  check((await W("/spotify/callback?code=sp-code&state=WRONG", { headers: { Cookie: C.Cookie + "; catego_sp=" + sp } })).headers.get("Location") === "/app?sp=error#np", "spotify state mismatch");
  const st = (await (await W("/api/state", { headers: C })).json());
  const page = await (await W(st.np.path)).text();
  check(page.includes("api.spotify.com") && page.includes('data-theme="vinyl"'), "np widget page");
  check(!(await (await W("/np/123-wrongkeywrongkey")).text()).includes("api.spotify.com"), "np wrong key → empty");
});

console.log(failed ? `\n${failed} FAILED (${passed} checks passed)` : `\nAll web tests passed (${passed} checks)`);
process.exit(failed ? 1 : 0);
