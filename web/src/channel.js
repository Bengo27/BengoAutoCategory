// אובייקט אחד לכל סטרימר (Durable Object): ההתחברות לקיק, ההגדרות, תור ההקראות,
// והחיבורים הפתוחים (הווידג'ט ב-OBS + לוח הבקרה באתר).
import {
  KickError, createReward, updateReward, pendingRedemptions, resolveRedemptions, userNames,
  subscribeRedemptions, refreshTokens, KICK,
} from "./kick.js";
import { defaultSettings, sanitizeSettings, checkMessage, applyTemplate, synthesize } from "./tts.js";
import { seal, unseal, randomId } from "./crypto.js";

const KEEP_FINISHED = 40;
const REWARD_COLOR = "#2DD4BF";
const POLL_WITH_WEBHOOK = 60_000; // גיבוי – למקרה ש-Webhook לא הגיע
const POLL_WITHOUT_WEBHOOK = 5_000;

const json = (data, status = 200) =>
  new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json", "Cache-Control": "no-store" } });

export class ChannelDO {
  constructor(state, env) {
    this.state = state;
    this.env = env;
    this.audio = new Map(); // id → mp3 (בזיכרון בלבד)
    this.data = null;
    state.blockConcurrencyWhile(async () => {
      this.data = (await state.storage.get("data")) || null;
    });
  }

  now() { return Date.now(); }

  async save() { await this.state.storage.put("data", this.data); }

  /** חיבורים פתוחים (בלי אלה שבאמצע סגירה). */
  sockets(role) { return this.state.getWebSockets(role).filter((ws) => ws.readyState !== 2 && ws.readyState !== 3); }

  // ───────────────────────── ניתוב ─────────────────────────

  async fetch(request) {
    const url = new URL(request.url);
    const op = url.pathname.slice(1);
    const body = request.method === "POST" ? await request.json().catch(() => ({})) : {};
    try {
      if (op === "init") return json(await this.init(body));
      if (op === "webhook") { await this.onWebhook(body); return json({ ok: true }); }

      if (!this.data) return json({ error: "not_found" }, 404);

      if (op === "ws") return this.acceptSocket(request, url);
      if (op === "overlay") return json({ ok: this.checkKey(url.searchParams.get("key")) }, this.checkKey(url.searchParams.get("key")) ? 200 : 403);
      if (op === "audio") return this.serveAudio(url);
      if (op === "state") return json(this.view());

      switch (op) {
        case "settings": await this.updateSettings(body.settings); break;
        case "enable": await this.setEnabled(!!body.enabled); break;
        case "reward": await this.ensureReward(); break;
        case "skip": await this.skip(); break;
        case "pause": this.data.paused = !!body.paused; await this.save(); await this.pump(); break;
        case "approve": await this.approve(String(body.id || "")); break;
        case "reject": await this.rejectById(String(body.id || "")); break;
        case "test": await this.enqueueTest(String(body.text || "")); break;
        case "rotate": this.data.overlayKey = randomId(18); await this.save(); this.closeOverlays(); break;
        case "voice-test": return await this.voiceTest(body);
        case "logout": await this.logout(); break;
        default: return json({ error: "unknown" }, 404);
      }
      this.broadcast();
      return json({ ok: true, state: this.view() });
    } catch (e) {
      const message = e instanceof KickError || e instanceof Error ? e.message : "שגיאה";
      if (this.data) { this.data.error = message; await this.save().catch(() => {}); this.broadcast(); }
      return json({ error: message }, e?.status && e.status >= 400 && e.status < 500 ? 400 : 500);
    }
  }

  // ───────────────────────── התחברות ─────────────────────────

  async init({ user, tokens }) {
    const fresh = !this.data;
    this.data ||= {
      settings: defaultSettings(), overlayKey: randomId(18), rewardId: null, webhook: false,
      items: [], seen: [], paused: false, playing: null, gapUntil: 0, lastPoll: 0, error: null, tests: [],
    };
    this.data.user = { id: user.id, name: user.name, slug: user.slug };
    this.data.auth = {
      access: await seal(this.env, tokens.access),
      refresh: tokens.refresh ? await seal(this.env, tokens.refresh) : this.data.auth?.refresh,
      expires: tokens.expires,
      scope: tokens.scope || "",
    };
    this.data.error = null;
    await this.save();
    // אם כבר יש פרס – מוודאים שההתראות פעילות
    if (!fresh && this.data.rewardId && !this.data.webhook) await this.trySubscribe();
    this.broadcast();
    return { ok: true };
  }

  async logout() {
    const a = this.data.auth;
    if (a?.refresh) {
      try {
        const token = await unseal(this.env, a.refresh);
        await fetch(`${KICK.revoke}?token=${encodeURIComponent(token)}&token_hint_type=refresh_token`, { method: "POST" });
      } catch {}
    }
    this.data.auth = null;
    await this.save();
    for (const ws of this.sockets("dashboard")) try { ws.close(4001, "logout"); } catch {}
  }

  hasRewardsScope() {
    const scope = this.data.auth?.scope || "";
    return !scope || scope.split(" ").includes("channel:rewards:write");
  }

  async accessToken(force = false) {
    const a = this.data.auth;
    if (!a) throw new KickError("לא מחובר לקיק – התחבר מחדש.", 401);
    if (force || this.now() > a.expires - 60_000) {
      if (!a.refresh) throw new KickError("החיבור לקיק פג – התחבר מחדש.", 401);
      let t;
      try {
        t = await refreshTokens(this.env, await unseal(this.env, a.refresh));
      } catch (e) {
        if (e.status === 400 || e.status === 401) {
          this.data.auth = null;
          await this.save();
          throw new KickError("החיבור לקיק פג – התחבר מחדש.", 401);
        }
        throw e;
      }
      this.data.auth = {
        access: await seal(this.env, t.access),
        refresh: t.refresh ? await seal(this.env, t.refresh) : a.refresh,
        expires: t.expires,
        scope: t.scope || a.scope,
      };
      await this.save();
    }
    return unseal(this.env, this.data.auth.access);
  }

  /** קריאה לקיק עם רענון אוטומטי פעם אחת אחרי 401. */
  async kick(fn) {
    try {
      return await fn(await this.accessToken());
    } catch (e) {
      if (e.status !== 401 || !this.data.auth) throw e;
      return fn(await this.accessToken(true));
    }
  }

  // ───────────────────────── הגדרות ופרס ─────────────────────────

  async updateSettings(input) {
    const before = this.data.settings;
    const after = sanitizeSettings({ ...input, enabled: undefined }, before);
    after.enabled = before.enabled;
    this.data.settings = after;
    await this.save();
    const rewardChanged = before.rewardTitle !== after.rewardTitle || before.rewardCost !== after.rewardCost ||
      before.rewardDescription !== after.rewardDescription;
    if (rewardChanged && this.data.rewardId && this.data.auth) await this.ensureReward();
  }

  async ensureReward() {
    if (!this.data.auth) throw new KickError("קודם צריך להתחבר לקיק.", 401);
    if (!this.hasRewardsScope()) throw new KickError("ההתחברות נעשתה בלי הרשאת פרסים – התנתק והתחבר שוב.", 403);
    const s = this.data.settings;
    if (this.data.rewardId) {
      try {
        await this.kick((t) => updateReward(t, this.data.rewardId, {
          title: s.rewardTitle, cost: s.rewardCost, description: s.rewardDescription,
          is_user_input_required: true, is_enabled: true, is_paused: !s.enabled,
        }));
      } catch (e) {
        if (e.status !== 404) throw e;
        this.data.rewardId = null; // נמחק בקיק – ניצור חדש
      }
    }
    if (!this.data.rewardId) {
      const r = await this.kick((t) => createReward(t, {
        title: s.rewardTitle, cost: s.rewardCost, description: s.rewardDescription, color: REWARD_COLOR,
      }));
      if (!r?.id) throw new KickError("קיק לא החזיר את הפרס שנוצר.", 502);
      this.data.rewardId = r.id;
      if (!s.enabled) await this.kick((t) => updateReward(t, r.id, { is_paused: true })).catch(() => {});
    }
    this.data.error = null;
    await this.save();
    await this.trySubscribe();
  }

  async trySubscribe() {
    try {
      await this.kick((t) => subscribeRedemptions(t));
      this.data.webhook = true;
    } catch {
      this.data.webhook = false; // נעבוד בבדיקה כל כמה שניות
    }
    await this.save();
  }

  async setEnabled(on) {
    this.data.settings.enabled = on;
    await this.save();
    if (on && !this.data.rewardId && this.data.auth) {
      await this.ensureReward();
    } else if (this.data.rewardId && this.data.auth) {
      await this.kick((t) => updateReward(t, this.data.rewardId, { is_paused: !on })).catch((e) => {
        this.data.error = (on ? "לא ניתן להפעיל" : "לא ניתן להשהות") + " את הפרס בקיק: " + e.message;
      });
    }
    if (on) {
      this.data.lastPoll = 0;
      await this.save();
      await this.tick();
    } else if (this.data.playing) {
      await this.skip();
    }
  }

  // ───────────────────────── בקשות מצופים ─────────────────────────

  async onWebhook(r) {
    if (!this.data || !r?.id) return;
    if (r.rewardId && this.data.rewardId && r.rewardId !== this.data.rewardId) return; // פרס אחר בערוץ
    if (!this.data.rewardId) return;
    if (r.status && r.status !== "pending") {
      // הסטרימר אישר/דחה בעצמו בקיק
      const item = this.data.items.find((i) => i.id === r.id && (i.status === "waiting" || i.status === "approval"));
      if (item) {
        item.status = r.status === "accepted" ? "done" : "rejected";
        item.note = "טופל בקיק";
        await this.save();
        this.broadcast();
      }
      return;
    }
    await this.addRedemptions([r]);
  }

  async addRedemptions(list) {
    const s = this.data.settings;
    const fresh = list.filter((r) => !this.data.seen.includes(r.id));
    if (!fresh.length) return 0;

    const missing = fresh.filter((r) => !r.username && r.userId).map((r) => r.userId);
    let names = {};
    if (missing.length) names = await this.kick((t) => userNames(t, missing)).catch(() => ({}));

    const toReject = [];
    for (const r of fresh.sort((a, b) => a.at - b.at)) {
      this.data.seen.push(r.id);
      const check = checkMessage(r.input, s);
      const item = {
        id: r.id, user: r.username || names[r.userId] || "צופה", userId: r.userId || 0,
        text: check.ok ? check.text : String(r.input || "").trim().slice(0, 400), at: r.at || this.now(),
        status: s.manualApproval ? "approval" : "waiting", note: null, test: false,
      };
      if (!s.enabled) toReject.push([item, "ההקראה כבויה"]);
      else if (this.now() - item.at > s.maxAgeMinutes * 60_000) toReject.push([item, "בקשה ישנה"]);
      else if (!check.ok) toReject.push([item, check.reason]);
      this.data.items.push(item);
    }
    this.data.seen = this.data.seen.slice(-300);
    for (const [item, reason] of toReject) await this.reject(item, reason);
    this.trim();
    await this.save();
    this.broadcast();
    await this.pump();
    return fresh.length;
  }

  async poll() {
    this.data.lastPoll = this.now();
    if (!this.data.rewardId || !this.data.auth || !this.data.settings.enabled) return;
    try {
      const pending = await this.kick((t) => pendingRedemptions(t, this.data.rewardId));
      if (this.data.error && /חיבור|פרס|בקשות|שגיאה/.test(this.data.error)) this.data.error = null;
      await this.addRedemptions(pending);
    } catch (e) {
      this.data.error = e.status === 404 ? "פרס ההקראה לא נמצא בקיק (אולי נמחק). לחץ \"עדכן את הפרס בקיק\"." : e.message;
    }
    await this.save();
  }

  async reject(item, reason) {
    item.status = "rejected";
    item.note = reason;
    if (item.test) return;
    await this.kick((t) => resolveRedemptions(t, "reject", [item.id])).catch(() => { item.note += " (לא נמסר לקיק)"; });
  }

  async accept(item) {
    if (item.test) return;
    await this.kick((t) => resolveRedemptions(t, "accept", [item.id])).catch(() => {});
  }

  async approve(id) {
    const item = this.data.items.find((i) => i.id === id && i.status === "approval");
    if (!item) return;
    item.status = "waiting";
    await this.save();
    await this.pump();
  }

  async rejectById(id) {
    const item = this.data.items.find((i) => i.id === id && (i.status === "waiting" || i.status === "approval"));
    if (!item) return;
    await this.reject(item, "נדחה על ידי הסטרימר");
    await this.save();
  }

  async enqueueTest(text) {
    const check = checkMessage(text || "בדיקת קול", { ...defaultSettings(), blockLinks: false, banned: [], maxChars: 300 });
    this.data.items.unshift({
      id: "test-" + randomId(8), user: "בדיקה", userId: 0, text: check.text || "בדיקת קול",
      at: this.now(), status: "waiting", note: null, test: true,
    });
    await this.save();
    if (!this.sockets("overlay").length) this.data.error = "הווידג'ט לא פתוח ב-OBS – הבדיקה תושמע כשהוא ייפתח.";
    await this.pump();
  }

  async voiceTest({ text, voice, rate }) {
    const t = this.now();
    this.data.tests = (this.data.tests || []).filter((x) => t - x < 60_000);
    if (this.data.tests.length >= 10) return json({ error: "יותר מדי בדיקות – חכה דקה." }, 429);
    this.data.tests.push(t);
    await this.save();
    const s = this.data.settings;
    const msg = checkMessage(text || "בדיקת קול", { ...defaultSettings(), blockLinks: false, banned: [], maxChars: 300 }).text || "בדיקת קול";
    const mp3 = await synthesize(this.env, msg, voice || s.voice, Number.isFinite(rate) ? rate : s.rate);
    return new Response(mp3, { headers: { "Content-Type": "audio/mpeg", "Cache-Control": "no-store" } });
  }

  // ───────────────────────── הקראה ─────────────────────────

  /** מתחיל את ההקראה הבאה אם אפשר. */
  async pump() {
    const d = this.data;
    if (d.playing) return;
    if (!this.sockets("overlay").length) { await this.scheduleAlarm(); return; }
    if (this.now() < (d.gapUntil || 0)) { await this.scheduleAlarm(); return; }
    const item = d.items.find((i) => i.status === "waiting" && (i.test || (d.settings.enabled && !d.paused)));
    if (!item) { await this.scheduleAlarm(); return; }

    item.status = "playing";
    d.playing = { id: item.id, deadline: this.now() + 60_000 };
    await this.save();
    this.broadcast();

    let mp3;
    try {
      const s = d.settings;
      mp3 = await synthesize(this.env, applyTemplate(s.template, item.user, item.text), s.voice, s.rate);
    } catch (e) {
      item.status = "failed";
      item.note = e.message;
      d.playing = null;
      if (!item.test) await this.kick((t) => resolveRedemptions(t, "reject", [item.id])).catch(() => {});
      await this.save();
      this.broadcast();
      return this.pump();
    }

    this.audio.set(item.id, mp3);
    d.playing.deadline = this.now() + Math.ceil(mp3.byteLength / 6000) * 1000 + 15_000;
    await this.save();
    this.sendOverlays({ type: "play", id: item.id, user: item.user, text: item.text, volume: d.settings.volume / 100 });
    await this.scheduleAlarm();
  }

  async finish(id, status) {
    const d = this.data;
    if (!d.playing || d.playing.id !== id) return;
    const item = d.items.find((i) => i.id === id);
    d.playing = null;
    this.audio.delete(id);
    d.gapUntil = this.now() + d.settings.gapSeconds * 1000;
    if (item) {
      item.status = status;
      await this.accept(item); // הוקרא או דולג → הנקודות נוצלו
    }
    this.trim();
    await this.save();
    this.broadcast();
    await this.pump();
  }

  async skip() {
    const p = this.data.playing;
    if (!p) return;
    this.sendOverlays({ type: "stop", id: p.id });
    await this.finish(p.id, "skipped");
  }

  /** בדיקות תקופתיות: סיום הקראה שנתקעה, גיבוי לבקשות שפספסנו, והקראה הבאה. */
  async tick() {
    const d = this.data;
    if (!d) return;
    if (d.playing && this.now() >= d.playing.deadline) await this.finish(d.playing.id, "done");
    const interval = d.webhook ? POLL_WITH_WEBHOOK : POLL_WITHOUT_WEBHOOK;
    if (this.sockets("overlay").length && d.settings.enabled && d.rewardId && this.now() - (d.lastPoll || 0) >= interval) {
      await this.poll();
    }
    await this.pump();
  }

  async alarm() {
    await this.tick();
    await this.scheduleAlarm();
  }

  async scheduleAlarm() {
    const d = this.data;
    const times = [];
    if (d.playing) times.push(d.playing.deadline);
    if ((d.gapUntil || 0) > this.now()) times.push(d.gapUntil);
    if (this.sockets("overlay").length && d.settings.enabled && d.rewardId) {
      times.push((d.lastPoll || 0) + (d.webhook ? POLL_WITH_WEBHOOK : POLL_WITHOUT_WEBHOOK));
    }
    if (times.length) await this.state.storage.setAlarm(Math.max(this.now() + 500, Math.min(...times)));
    else await this.state.storage.deleteAlarm();
  }

  trim() {
    const items = this.data.items;
    const finished = items.filter((i) => !["waiting", "approval", "playing"].includes(i.status));
    const drop = new Set(finished.slice(0, Math.max(0, finished.length - KEEP_FINISHED)).map((i) => i.id));
    this.data.items = items.filter((i) => !drop.has(i.id));
  }

  // ───────────────────────── חיבורים (WebSocket) ─────────────────────────

  checkKey(key) { return !!key && key === this.data.overlayKey; }

  acceptSocket(request, url) {
    if (request.headers.get("Upgrade") !== "websocket") return new Response("expected websocket", { status: 426 });
    const role = url.searchParams.get("role") === "overlay" ? "overlay" : "dashboard";
    if (role === "overlay" && !this.checkKey(url.searchParams.get("key"))) return new Response("forbidden", { status: 403 });
    const pair = new WebSocketPair();
    const [client, server] = Object.values(pair);
    this.state.acceptWebSocket(server, [role]);
    if (role === "dashboard") {
      server.send(JSON.stringify({ type: "state", state: this.view() }));
    } else {
      // ווידג'ט נפתח: לבדוק מיד בקשות ולהמשיך את התור
      this.data.lastPoll = 0;
      const work = (async () => { this.broadcast(); await this.tick(); await this.scheduleAlarm(); })().catch(() => {});
      this.state.waitUntil?.(work);
    }
    return new Response(null, { status: 101, webSocket: client });
  }

  async webSocketMessage(ws, message) {
    let m;
    try { m = JSON.parse(typeof message === "string" ? message : new TextDecoder().decode(message)); } catch { return; }
    if (m.type === "done" && m.id) await this.finish(String(m.id), "done");
  }

  async webSocketClose(ws) {
    try { ws.close(); } catch {}
    this.broadcast();
  }

  async webSocketError() { this.broadcast(); }

  sendOverlays(msg) {
    const text = JSON.stringify(msg);
    for (const ws of this.sockets("overlay")) try { ws.send(text); } catch {}
  }

  closeOverlays() {
    for (const ws of this.sockets("overlay")) try { ws.close(4003, "key changed"); } catch {}
  }

  broadcast() {
    if (!this.data) return;
    const text = JSON.stringify({ type: "state", state: this.view() });
    for (const ws of this.sockets("dashboard")) try { ws.send(text); } catch {}
  }

  serveAudio(url) {
    if (!this.checkKey(url.searchParams.get("key"))) return new Response("forbidden", { status: 403 });
    const mp3 = this.audio.get(url.searchParams.get("id") || "");
    if (!mp3) return new Response("gone", { status: 404 });
    return new Response(mp3, { headers: { "Content-Type": "audio/mpeg", "Cache-Control": "no-store" } });
  }

  /** מה שלוח הבקרה מציג. */
  view() {
    const d = this.data;
    const open = d.items.filter((i) => ["playing", "waiting", "approval"].includes(i.status));
    const done = d.items.filter((i) => !["playing", "waiting", "approval"].includes(i.status)).slice(-15).reverse();
    return {
      user: d.user,
      connected: !!d.auth,
      rewardsScope: !!d.auth && this.hasRewardsScope(),
      settings: d.settings,
      rewardId: d.rewardId,
      webhook: d.webhook,
      overlays: this.sockets("overlay").length,
      overlayPath: `/o/${d.user?.id}-${d.overlayKey}`,
      paused: !!d.paused,
      playingId: d.playing?.id || null,
      error: d.error,
      items: [...open.sort((a, b) => (a.status === "playing" ? -1 : b.status === "playing" ? 1 : 0)), ...done]
        .map(({ id, user, text, at, status, note, test }) => ({ id, user, text, at, status, note, test })),
    };
  }
}
