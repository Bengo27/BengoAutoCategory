// אובייקט אחד לכל סטרימר (Durable Object): ההתחברות לקיק, ההגדרות, תור ההקראות,
// והחיבורים הפתוחים (הווידג'ט ב-OBS + לוח הבקרה באתר).
import {
  KickError, createReward, updateReward, pendingRedemptions, resolveRedemptions, userNames,
  subscribeRedemptions, subscribeEvents, unsubscribeEvent, getChannelInfo, refreshTokens, KICK, REDEMPTION_EVENT, CHAT_EVENT, ALERT_EVENTS,
} from "./kick.js";
import { defaultSettings, sanitizeSettings, checkMessage, applyTemplate, synthesize, synthesizeGemini, chatCommandText, chatAllowed, alertText, ALERT_KINDS } from "./tts.js";

/** פריט שקשור לקנייה בקיק (ולא בדיקה או הודעת צ'אט) – רק לו מאשרים/מחזירים נקודות. */
const isRedemption = (item) => !item.test && !item.chat && !item.alert;
const spokenText = (s, item) => item.spoken || applyTemplate(s.template, item.user, item.text);
const ALERT_FLAG = { kicks: "alertKicks", follow: "alertFollow", sub: "alertSub", gift: "alertGift" };
import { seal, unseal, randomId } from "./crypto.js";
import { npDefaults, sanitizeNp, validClientId, spotifyExchange, spotifyRefresh } from "./nowplaying.js";

const KEEP_FINISHED = 40;
const REWARD_COLOR = "#2DD4BF";
const POLL_WITH_WEBHOOK = 30_000; // גיבוי – למקרה ש-Webhook לא הגיע
const POLL_WITHOUT_WEBHOOK = 5_000;

const json = (data, status = 200) =>
  new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json", "Cache-Control": "no-store" } });

function sampleAlert(kind) {
  const samples = {
    kicks: { kind, user: "דני_גיימר", amount: 100, text: "אחי איזה משחק, תמשיך ככה!" },
    follow: { kind, user: "צופה_חדש" },
    sub: { kind, user: "יוסי123", months: 3 },
    gift: { kind, user: "המלך", count: 5 },
  };
  if (!samples[kind]) { const e = new Error("סוג בדיקה לא מוכר"); e.status = 400; throw e; }
  return { ...samples[kind], id: randomId(8) };
}

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

  /** מצב Edge: הקול יוצא מלשונית של לוח הבקרה. מצב שרת: הווידג'ט ב-OBS מנגן MP3 מ-Azure. */
  edgeMode() { const p = this.data.settings.playback; return p !== "server" && p !== "gemini"; }

  /** קול מהשרת: Gemini או Azure. אם Gemini נכשל (למשל מגבלה יומית) ו-Azure מוגדר – מנסים את Azure. */
  async synth(text, voiceOverride, rateOverride) {
    const s = this.data.settings;
    const rate = Number.isFinite(rateOverride) ? rateOverride : s.rate;
    if (s.playback === "gemini") {
      const d = this.data;
      try {
        // אחרי "הגענו למגבלה" לא מנסים שוב 10 דקות (כל ניסיון מבזבז כמה שניות)
        if ((d.geminiLimitedUntil || 0) > this.now()) throw new Error("Gemini הגיע למגבלה של המסלול החינמי – ננסה שוב בעוד כמה דקות.");
        return await synthesizeGemini(this.env, text, voiceOverride || s.geminiVoice, rate);
      } catch (e) {
        if (/מגבלה/.test(e.message) && !((d.geminiLimitedUntil || 0) > this.now())) { d.geminiLimitedUntil = this.now() + 10 * 60_000; await this.save(); }
        if (!(this.env.AZURE_SPEECH_KEY && this.env.AZURE_SPEECH_REGION)) throw e;
      }
    }
    const mp3 = await synthesize(this.env, text, s.playback === "gemini" ? s.voice : voiceOverride || s.voice, rate);
    return { data: mp3, type: "audio/mpeg" };
  }

  /** יש מי שישמיע את הקול עכשיו? */
  canPlay() { return this.edgeMode() ? this.sockets("speaker").length > 0 : this.sockets("overlay").length > 0; }

  /** משהו פתוח (ווידג'ט או לשונית מקריאה) – כדאי לבדוק בקשות. */
  anyOutput() { return this.sockets("overlay").length > 0 || this.sockets("speaker").length > 0; }

  // ───────────────────────── ניתוב ─────────────────────────

  async fetch(request) {
    const url = new URL(request.url);
    const op = url.pathname.slice(1);
    const body = request.method === "POST" ? await request.json().catch(() => ({})) : {};
    try {
      if (op === "init") return json(await this.init(body));
      if (op === "webhook") { await this.onWebhook(body); if (this.data) await this.save(); return json({ ok: true }); }
      if (op === "alert") { await this.onAlert(body); return json({ ok: true }); }
      if (op === "chat") { await this.onChat(body); await this.save(); return json({ ok: true }); }
      if (op === "hook-bad") { if (this.data) { this.data.badHookAt = this.now(); await this.save(); this.broadcast(); } return json({ ok: true }); }

      if (!this.data) return json({ error: "not_found" }, 404);

      if (op === "ws") return this.acceptSocket(request, url);
      if (op === "overlay") return json({ ok: this.checkKey(url.searchParams.get("key")) }, this.checkKey(url.searchParams.get("key")) ? 200 : 403);
      if (op === "audio") return this.serveAudio(url);
      if (op === "state") { await this.refreshChannel(); return json(this.view()); }

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
        case "clear": await this.clearQueue(); break;
        case "alert-test": await this.onAlert(sampleAlert(String(body.kind || "")), true); break;
        case "chat-resync": this.data.chatSubscribed = false; await this.syncChatSubscription(); if (!this.data.chatSubscribed) throw new Error(this.data.error || "ההרשמה לצ'אט נכשלה"); this.data.error = null; break;
        case "np-settings": this.np().settings = sanitizeNp(body.settings, this.np().settings); await this.save(); this.sendNpConfig(); break;
        case "spotify-client": await this.setSpotifyClient(body.clientId); break;
        case "spotify-tokens": await this.spotifyConnect(body); break;
        case "spotify-disconnect": await this.spotifyDisconnect(); break;
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
    // אחרי התחברות מחדש (למשל בשביל הרשאת Kicks) – מרעננים את ההרשמות לאירועים
    const st = this.data.settings;
    if (!fresh && (st.chatEnabled || st.alertKicks || st.alertFollow || st.alertSub || st.alertGift)) await this.syncChatSubscription().catch(() => {});
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
    const eventsChanged = before.chatEnabled !== after.chatEnabled || ALERT_KINDS.some((k) => before[ALERT_FLAG[k]] !== after[ALERT_FLAG[k]]);
    if (eventsChanged && this.data.auth) await this.syncChatSubscription();
  }

  /** הקראה מהצ'אט צריכה התראות על הודעות צ'אט. כשמכבים – מבטלים, כדי לחסוך בקשות לשרת. */
  async syncChatSubscription() {
    // צ'אט + אירועים להקראה: נרשמים למה שמופעל, ומבטלים את השאר (חוסך בקשות לשרת)
    const st = this.data.settings;
    const want = { chat: !!st.chatEnabled };
    for (const k of ALERT_KINDS) want[k] = !!st[ALERT_FLAG[k]];
    const events = { chat: [CHAT_EVENT], ...ALERT_EVENTS };
    const subs = {};
    const off = Object.keys(events).filter((k) => !want[k]).flatMap((k) => events[k]);
    if (off.length) await this.kick((t) => unsubscribeEvent(t, off)).catch(() => {});
    for (const k of Object.keys(events).filter((k) => want[k])) {
      if (k === "kicks" && !this.hasScope("kicks:read")) { subs[k] = "צריך להתחבר מחדש לקיק (הרשאת Kicks)"; continue; }
      try { await this.kick((t) => subscribeEvents(t, events[k])); subs[k] = true; }
      catch (e) { subs[k] = e.message || "ההרשמה נכשלה"; }
    }
    this.data.alertSubs = subs;
    this.data.chatSubscribed = subs.chat === true;
    if (want.chat && subs.chat !== true) this.data.error = "לא ניתן להירשם להודעות הצ'אט בקיק: " + subs.chat;
    await this.save();
  }

  hasScope(name) { return String(this.data.auth?.scope || "").split(/\s+/).includes(name); }

  /** אירוע מקיק להקראה (Kicks / עוקב / סאב / סאבים במתנה). test = מכפתור הבדיקה. */
  async onAlert(ev, test = false) {
    const d = this.data;
    if (!d || !ev?.kind || !ALERT_KINDS.includes(ev.kind)) return;
    if (!test) await this.markHook();
    const s = d.settings;
    const note = async (reason) => { d.alertDebug = { at: this.now(), kind: ev.kind, user: ev.user || "", reason }; await this.save(); this.broadcast(); };
    if (!test) {
      if (!s.enabled) return note("ההקראה כבויה (המתג הראשי בסקירה)");
      if (!s[ALERT_FLAG[ev.kind]]) return note("ההקראה לסוג הזה כבויה");
      if (ev.kind === "kicks" && Number(ev.amount || 0) < s.alertKicksMin) return note("מתחת לסכום המינימלי (" + s.alertKicksMin + ")");
    }
    const id = (test ? "test-alert-" : "alert-") + (ev.id || randomId(10));
    if (d.seen.includes(id)) return;
    let msg = String(ev.text || "");
    if (msg) { const c = checkMessage(msg, s); msg = c.ok ? c.text : ""; } // הודעה חסומה – מקריאים בלי ההודעה
    const spoken = alertText(ev.kind, s, { ...ev, text: msg });
    d.seen.push(id); d.seen = d.seen.slice(-300);
    d.items.push({ id, user: ev.user || "צופה", userId: ev.userId || 0, text: spoken, spoken, at: this.now(),
      status: "waiting", note: null, test, alert: ev.kind });
    d.alertDebug = { at: this.now(), kind: ev.kind, user: ev.user || "", reason: test ? "בדיקה – נכנס לתור ✓" : "נכנס לתור ✓" };
    if (test && !this.canPlay()) {
      d.error = this.edgeMode() ? "אף לשונית לא מקריאה כרגע – לחץ \"הפעל הקראה מהלשונית הזו\" (ב-Edge)." : "הווידג'ט לא פתוח ב-OBS – הבדיקה תושמע כשהוא ייפתח.";
    }
    this.trim();
    await this.save();
    this.broadcast();
    await this.pump();
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

  /** הודעה בצ'אט שמתחילה בסימן ההקראה. */
  /** Webhook באמת מגיע? (נרשמנו + הגיעה התראה ב-24 השעות האחרונות) */
  hooksWork() { const d = this.data; return !!d.webhook && this.now() - (d.lastHookAt || 0) < 24 * 3600_000; }

  async markHook() { this.data.lastHookAt = this.now(); this.data.badHookAt = 0; }

  async onChat(m) {
    const d = this.data;
    if (!d || !m?.id) return;
    await this.markHook();
    const s = d.settings;
    // מה קרה להודעה האחרונה מהצ'אט – מוצג בלוח הבקרה כדי שיהיה ברור למה משהו לא הוקרא
    const note = async (reason) => { d.chatDebug = { at: this.now(), user: m.username || "", text: String(m.content || "").slice(0, 80), reason }; await this.save(); this.broadcast(); };
    if (!s.enabled) return note("ההקראה כבויה (המתג הראשי בסקירה)");
    if (!s.chatEnabled) return note("הקראה מהצ'אט כבויה");
    const raw = chatCommandText(m.content, s.chatPrefix);
    if (!raw) return note("לא מתחיל ב-" + s.chatPrefix + " ורווח");
    const id = "chat-" + m.id;
    if (d.seen.includes(id)) return;
    if (!chatAllowed(s.chatWho, m.badges || [])) return note("לצופה הזה אין הרשאה (מי יכול להקריא)");
    const isMod = (m.badges || []).some((b) => b === "broadcaster" || b === "moderator");
    d.chatLast ||= {};
    const last = d.chatLast[m.userId] || 0;
    if (!isMod && s.chatCooldown > 0 && this.now() - last < s.chatCooldown * 1000) return note("המתנה בין הודעות (" + s.chatCooldown + " שנ')");
    const check = checkMessage(raw, s);
    if (!check.ok) return note("נחסם בסינון: " + (check.reason || "תוכן לא מתאים")); // בצ'אט אין נקודות להחזיר
    d.chatDebug = { at: this.now(), user: m.username || "", text: String(m.content || "").slice(0, 80), reason: "נכנס לתור ✓" };
    d.seen.push(id);
    d.seen = d.seen.slice(-300);
    d.chatLast[m.userId] = this.now();
    // ניקוי רשומות השהיה ישנות
    for (const [k, t] of Object.entries(d.chatLast)) if (this.now() - t > 3600_000) delete d.chatLast[k];
    d.items.push({
      id, user: m.username || "צופה", userId: m.userId || 0, text: check.text, at: m.at || this.now(),
      status: s.manualApproval ? "approval" : "waiting", note: null, test: false, chat: true,
    });
    this.trim();
    await this.save();
    this.broadcast();
    await this.pump();
  }

  async onWebhook(r) {
    if (!this.data || !r?.id) return;
    await this.markHook();
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
    if (!isRedemption(item)) return;
    await this.kick((t) => resolveRedemptions(t, "reject", [item.id])).catch(() => { item.note += " (לא נמסר לקיק)"; });
  }

  async accept(item) {
    if (!isRedemption(item)) return;
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

  /** מנקה את התור: בקשות מפרסים נדחות (הנקודות חוזרות), הודעות צ'אט ובדיקות נמחקות. */
  async clearQueue() {
    for (const item of this.data.items.filter((i) => i.status === "waiting" || i.status === "approval")) {
      await this.reject(item, "התור נוקה");
    }
    await this.save();
  }

  /** מצב הערוץ בקיק (לייב / צופים) – לכל היותר פעם בדקה. */
  async refreshChannel(force = false) {
    const d = this.data;
    if (!d?.auth) return;
    if (!force && d.channel && this.now() - (d.channel.at || 0) < 60_000) return;
    try {
      const info = await this.kick((t) => getChannelInfo(t));
      if (info) { d.channel = { ...info, at: this.now() }; await this.save(); }
    } catch {}
  }

  /** מונים יומיים לסקירה. */
  count(item) {
    const d = this.data;
    const day = new Date(this.now() + 3 * 3600_000).toISOString().slice(0, 10); // שעון ישראל (בערך)
    if (!d.stats || d.stats.day !== day) d.stats = { day, total: 0, chat: 0, reward: 0 };
    if (item.test) return;
    d.stats.total++;
    if (item.chat) d.stats.chat++; else if (item.alert) d.stats.alert = (d.stats.alert || 0) + 1; else d.stats.reward++;
  }

  async enqueueTest(text) {
    const check = checkMessage(text || "בדיקת קול", { ...defaultSettings(), blockLinks: false, banned: [], maxChars: 300 });
    this.data.items.push({
      id: "test-" + randomId(8), user: "בדיקה", userId: 0, text: check.text || "בדיקת קול",
      at: this.now(), status: "waiting", note: null, test: true,
    });
    await this.save();
    if (!this.canPlay()) {
      this.data.error = this.edgeMode()
        ? "אף לשונית לא מקריאה כרגע – לחץ \"הפעל הקראה מהלשונית הזו\" בלוח הבקרה (ב-Edge)."
        : "הווידג'ט לא פתוח ב-OBS – הבדיקה תושמע כשהוא ייפתח.";
    }
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
    const a = await this.synth(msg, voice, rate);
    return new Response(a.data, { headers: { "Content-Type": a.type, "Cache-Control": "no-store" } });
  }

  // ───────────────────────── הקראה ─────────────────────────

  /** מתחיל את ההקראה הבאה אם אפשר. */
  async pump() {
    const d = this.data;
    if (d.playing) return;
    if (!this.canPlay()) { await this.scheduleAlarm(); return; }
    if (this.now() < (d.gapUntil || 0)) { await this.scheduleAlarm(); return; }
    // בדיקות קודם, ואז הודעות של צופים לפי הסדר
    const item = d.items.find((i) => i.status === "waiting" && i.test) ||
      d.items.find((i) => i.status === "waiting" && !i.test && d.settings.enabled && !d.paused);
    if (!item) { await this.scheduleAlarm(); return; }

    item.status = "playing";
    d.playing = { id: item.id, deadline: this.now() + 60_000 };
    await this.save();
    this.broadcast();

    const s0 = d.settings;
    if (this.edgeMode()) {
      await this.speakViaTab(item);
      return;
    }

    let audio;
    try {
      const s = d.settings;
      audio = await this.synth(spokenText(s, item));
    } catch (e) {
      // אם יש לשונית Edge פתוחה – מקריאים ממנה במקום לוותר
      if (this.sockets("speaker").length) {
        item.note = "השרת נכשל – הוקרא מהלשונית";
        await this.speakViaTab(item);
        return;
      }
      item.status = "failed";
      item.note = e.message;
      d.playing = null;
      d.error = "ההקראה נכשלה: " + e.message;
      if (isRedemption(item)) await this.kick((t) => resolveRedemptions(t, "reject", [item.id])).catch(() => {});
      await this.save();
      this.broadcast();
      return this.pump();
    }
    if (d.error && d.error.startsWith("ההקראה נכשלה")) d.error = null;

    this.audio.set(item.id, audio);
    const bytesPerSec = audio.type === "audio/wav" ? 48_000 : 6_000;
    d.playing.deadline = this.now() + Math.ceil(audio.data.byteLength / bytesPerSec) * 1000 + 15_000;
    await this.save();
    this.sendOverlays({ type: "play", id: item.id, user: item.user, text: item.text, volume: d.settings.volume / 100 });
    await this.scheduleAlarm();
  }

  /** הקול מהלשונית ב-Edge (חינם), והווידג'ט ב-OBS רק מציג את ההודעה. */
  async speakViaTab(item) {
    const d = this.data;
    const s0 = d.settings;
    const spoken = spokenText(s0, item);
    d.playing = { id: item.id, deadline: this.now() + Math.ceil(spoken.length / 10) * 1000 + 15_000 };
    await this.save();
    this.broadcast();
    const speaker = this.sockets("speaker")[0];
    try {
      speaker.send(JSON.stringify({ type: "speak", id: item.id, text: spoken, voice: s0.voice, rate: s0.rate, volume: s0.volume / 100 }));
    } catch {}
    this.sendOverlays({ type: "play", id: item.id, user: item.user, text: item.text, silent: true });
    await this.scheduleAlarm();
  }

  async finish(id, status, note = null) {
    const d = this.data;
    if (!d.playing || d.playing.id !== id) return;
    const item = d.items.find((i) => i.id === id);
    d.playing = null;
    this.audio.delete(id);
    this.sendOverlays({ type: "end", id });
    d.gapUntil = this.now() + d.settings.gapSeconds * 1000;
    if (item) {
      item.status = status;
      if (note) item.note = note;
      if (status === "done") this.count(item);
      if (status === "failed") {
        if (isRedemption(item)) await this.kick((t) => resolveRedemptions(t, "reject", [item.id])).catch(() => {}); // מחזירים נקודות
      } else {
        await this.accept(item); // הוקרא או דולג → הנקודות נוצלו
      }
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
    for (const ws of this.sockets("speaker")) try { ws.send(JSON.stringify({ type: "stop", id: p.id })); } catch {}
    await this.finish(p.id, "skipped");
  }

  /** בדיקות תקופתיות: סיום הקראה שנתקעה, גיבוי לבקשות שפספסנו, והקראה הבאה. */
  async tick() {
    const d = this.data;
    if (!d) return;
    if (d.playing && this.now() >= d.playing.deadline) await this.finish(d.playing.id, "done");
    const interval = this.hooksWork() ? POLL_WITH_WEBHOOK : POLL_WITHOUT_WEBHOOK;
    if (this.anyOutput() && d.settings.enabled && d.rewardId && this.now() - (d.lastPoll || 0) >= interval) {
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
    if (this.anyOutput() && d.settings.enabled && d.rewardId) {
      times.push((d.lastPoll || 0) + (this.hooksWork() ? POLL_WITH_WEBHOOK : POLL_WITHOUT_WEBHOOK));
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
    const asked = url.searchParams.get("role");
    const role = ["overlay", "speaker", "np"].includes(asked) ? asked : "dashboard";
    if ((role === "overlay" || role === "np") && !this.checkKey(url.searchParams.get("key"))) return new Response("forbidden", { status: 403 });
    const pair = new WebSocketPair();
    const [client, server] = Object.values(pair);
    if (role === "np") {
      // ווידג'ט Now Playing: מקבל עיצוב + טוקן, ושואל את ספוטיפיי בעצמו
      this.state.acceptWebSocket(server, [role]);
      server.send(JSON.stringify({ type: "config", settings: this.np().settings }));
      this.broadcast();
      return new Response(null, { status: 101, webSocket: client });
    }
    // רק לשונית אחת מקריאה – החדשה מחליפה את הקודמת (אחרת ההקראה עלולה ללכת ללשונית בלי קולות)
    if (role === "speaker") for (const ws of this.sockets("speaker")) try { ws.close(4002, "replaced"); } catch {}
    this.state.acceptWebSocket(server, [role]);
    if (role !== "overlay") server.send(JSON.stringify({ type: "state", state: this.view() }));
    if (role === "dashboard") {
      const work = (async () => {
        if (this.data.settings.chatEnabled && !this.data.chatSubscribed && this.data.auth) await this.syncChatSubscription();
        await this.refreshChannel();
      })().then(() => this.broadcast()).catch(() => {});
      this.state.waitUntil?.(work);
    }
    if (role !== "dashboard") {
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
    if (m.type === "token" && this.sockets("np").includes(ws)) {
      const now = this.now();
      if (now - (ws.lastTokenAsk || 0) < 1000) return; // הגנה מהצפה
      ws.lastTokenAsk = now;
      const t = await this.spotifyToken().catch(() => null);
      try { ws.send(JSON.stringify({ type: "token", access: t?.access || null, expires: t?.expires || 0 })); } catch {}
      return;
    }
    // הווידג'ט (מצב שרת) או הלשונית המקריאה (מצב Edge) מדווחים שההקראה הסתיימה
    if (m.type === "done" && m.id) await this.finish(String(m.id), m.error ? "failed" : "done", m.error ? String(m.error).slice(0, 120) : null);
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
    for (const ws of [...this.sockets("overlay"), ...this.sockets("np")]) try { ws.close(4003, "key changed"); } catch {}
  }

  broadcast() {
    if (!this.data) return;
    const text = JSON.stringify({ type: "state", state: this.view() });
    for (const ws of [...this.sockets("dashboard"), ...this.sockets("speaker")]) try { ws.send(text); } catch {}
  }

  // ───────────────────────── Now Playing ─────────────────────────

  np() {
    this.data.np ||= { settings: npDefaults(), clientId: "", auth: null, error: null };
    return this.data.np;
  }

  sendNpConfig() {
    const text = JSON.stringify({ type: "config", settings: this.np().settings });
    for (const ws of this.sockets("np")) try { ws.send(text); } catch {}
  }

  async setSpotifyClient(id) {
    const clientId = String(id || "").trim();
    if (!validClientId(clientId)) { const e = new Error("ה-Client ID לא תקין (32 תווים מאתר המפתחים של ספוטיפיי)."); e.status = 400; throw e; }
    const np = this.np();
    if (np.clientId !== clientId) np.auth = null; // אפליקציה אחרת → צריך להתחבר מחדש
    np.clientId = clientId;
    np.error = null;
    await this.save();
  }

  async spotifyConnect({ code, verifier, redirect }) {
    const np = this.np();
    try {
      const t = await spotifyExchange(np.clientId, String(code || ""), String(verifier || ""), String(redirect || ""));
      np.auth = { access: await seal(this.env, t.access), refresh: await seal(this.env, t.refresh), expires: t.expires };
      np.error = null;
    } catch (e) {
      np.error = e.message; await this.save(); throw e;
    }
    await this.save();
    await this.pushNpTokens();
  }

  async spotifyDisconnect() {
    const np = this.np();
    np.auth = null; np.error = null;
    await this.save();
    for (const ws of this.sockets("np")) try { ws.send(JSON.stringify({ type: "token", access: null, expires: 0 })); } catch {}
  }

  /** טוקן בתוקף לספוטיפיי (מתחדש לבד). */
  async spotifyToken() {
    const np = this.np();
    if (!np.auth) return null;
    if (np.auth.expires - 60_000 > this.now()) return { access: await unseal(this.env, np.auth.access), expires: np.auth.expires };
    if (this.spRefreshing) return this.spRefreshing;
    this.spRefreshing = (async () => {
      try {
        const t = await spotifyRefresh(np.clientId, await unseal(this.env, np.auth.refresh));
        np.auth = { access: await seal(this.env, t.access), refresh: t.refresh ? await seal(this.env, t.refresh) : np.auth.refresh, expires: t.expires };
        np.error = null;
        await this.save();
        return { access: t.access, expires: t.expires };
      } catch (e) {
        if (e.code === "invalid_grant" || e.status === 400 || e.status === 401) { np.auth = null; np.error = e.message; await this.save(); this.broadcast(); }
        throw e;
      } finally { this.spRefreshing = null; }
    })();
    return this.spRefreshing;
  }

  async pushNpTokens() {
    const t = await this.spotifyToken().catch(() => null);
    const text = JSON.stringify({ type: "token", access: t?.access || null, expires: t?.expires || 0 });
    for (const ws of this.sockets("np")) try { ws.send(text); } catch {}
  }

  serveAudio(url) {
    if (!this.checkKey(url.searchParams.get("key"))) return new Response("forbidden", { status: 403 });
    const a = this.audio.get(url.searchParams.get("id") || "");
    if (!a) return new Response("gone", { status: 404 });
    return new Response(a.data, { headers: { "Content-Type": a.type, "Cache-Control": "no-store" } });
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
      speakers: this.sockets("speaker").length,
      serverTts: !!(this.env.AZURE_SPEECH_KEY && this.env.AZURE_SPEECH_REGION),
      geminiTts: !!this.env.GEMINI_API_KEY,
      overlayPath: `/o/${d.user?.id}-${d.overlayKey}`,
      paused: !!d.paused,
      playingId: d.playing?.id || null,
      error: d.error,
      items: [...open.sort((a, b) => (a.status === "playing" ? -1 : b.status === "playing" ? 1 : 0)), ...done]
        .map(({ id, user, text, at, status, note, test, chat, alert }) => ({ id, user, text, at, status, note, test, chat: !!chat, alert: alert || null })),
      chatSubscribed: !!d.chatSubscribed,
      lastHookAt: d.lastHookAt || 0, badHookAt: d.badHookAt || 0, chatDebug: d.chatDebug || null,
      alertSubs: d.alertSubs || {}, alertDebug: d.alertDebug || null, kicksScope: !!d.auth && this.hasScope("kicks:read"),
      np: (() => { const n = this.np(); return { settings: n.settings, clientId: n.clientId, connected: !!n.auth, error: n.error,
        widgets: this.sockets("np").length, path: `/np/${d.user?.id}-${d.overlayKey}` }; })(),
      channel: d.channel || null,
      stats: d.stats && d.stats.day === new Date(this.now() + 3 * 3600_000).toISOString().slice(0, 10) ? d.stats : { total: 0, chat: 0, reward: 0, alert: 0 },
    };
  }
}
