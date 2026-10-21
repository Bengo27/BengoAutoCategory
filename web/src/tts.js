// סינון הודעות + הקראה דרך Azure (אותם כללים כמו בתוכנה)
/** קולות Gemini (Google) – כולם מדברים עברית; השמות באנגלית. */
export const GEMINI_VOICES = {
  Kore: "Kore – אישה, רגועה",
  Aoede: "Aoede – אישה, קלילה",
  Zephyr: "Zephyr – אישה, שמחה",
  Puck: "Puck – גבר, אנרגטי",
  Charon: "Charon – גבר, רציני",
  Fenrir: "Fenrir – גבר, נלהב",
};
export const GEMINI_MODEL = "gemini-3.8-flash-tts";

export const VOICES = {
  "he-IL-HilaNeural": "Hila – אישה",
  "he-IL-AvriNeural": "Avri – גבר",
};

export const DEFAULT_BANNED = [
  "זונה", "שרמוטה", "בן זונה", "כוסאמק", "כוס אמק", "כוס אמא שלך", "מזדיין", "תזדיין", "זין שלי",
  "fuck", "fucking", "motherfucker", "shit", "bitch", "cunt", "whore", "nigger", "nigga", "faggot", "retard",
];

export function defaultSettings() {
  return {
    enabled: false,
    rewardTitle: "🔊 הקראה",
    rewardCost: 500,
    rewardDescription: "כתבו הודעה והיא תוקרא בקול בשידור",
    voice: "he-IL-HilaNeural",
    // "edge" = הקול מלשונית ב-Edge (חינם, בלי הגבלה) · "server" = Azure דרך הווידג'ט ב-OBS
    playback: "edge", // edge | server (Azure) | gemini
    geminiVoice: "Kore",
    rate: 0,
    volume: 80,
    template: "{user} אומר: {text}",
    maxChars: 200,
    blockLinks: true,
    manualApproval: false,
    maxAgeMinutes: 10,
    gapSeconds: 2,
    banned: [...DEFAULT_BANNED],
    // הקראה מהצ'אט: "!tts הודעה"
    chatEnabled: false,
    chatPrefix: "!tts",
    chatWho: "all", // all | subs | vip | mods
    chatCooldown: 60,
    // הקראת אירועים (Kicks / עוקב / סאב / סאבים במתנה) – ההתראה עצמה נשארת ב-Botrix, אצלנו רק הקול
    alertShowCard: false, // באירועים: רק קול (Botrix מציג את ההתראה). true = גם התיבה של ה-TTS
    alertKicks: false,
    alertKicksMin: 1,
    alertKicksTemplate: "{user} שלח {amount} קיקס: {text}",
    alertFollow: false,
    alertFollowTemplate: "תודה על העוקב, {user}!",
    alertSub: false,
    alertSubTemplate: "{user} עשה סאב! תודה!",
    alertGift: false,
    alertGiftTemplate: "{user} הביא {count} סאבים במתנה! תודה!",
  };
}

export const ALERT_KINDS = ["kicks", "follow", "sub", "gift"];
const ALERT_KEY = { kicks: "Kicks", follow: "Follow", sub: "Sub", gift: "Gift" };

/** בונה את הטקסט שיוקרא לאירוע. {user} {amount} {text} {months} {count} */
export function alertText(kind, s, ev) {
  const tpl = s["alert" + ALERT_KEY[kind] + "Template"] || defaultSettings()["alert" + ALERT_KEY[kind] + "Template"];
  const msg = (ev.text || "").trim();
  let t = tpl;
  // Kicks בלי הודעה: מורידים את החלק של ההודעה (": {text}")
  if (!msg) t = t.replace(/\s*[:\-–,]?\s*\{text\}/g, "!").replace(/!+\s*$/, "!");
  const vars = { user: ev.user || "צופה", amount: String(ev.amount ?? ""), text: msg, months: String(ev.months ?? ""), count: String(ev.count ?? "") };
  return t.replace(/\{(user|amount|text|months|count)\}/g, (_, k) => vars[k]).replace(/\s+/g, " ").trim().slice(0, 500);
}

const clamp = (n, lo, hi, d) => (Number.isFinite(Number(n)) ? Math.min(hi, Math.max(lo, Math.round(Number(n)))) : d);
const str = (s, max, d) => (typeof s === "string" && s.trim() ? s.trim().slice(0, max) : d);

/** ממזג שינויים מהמשתמש עם ההגדרות הקיימות, ומתקן ערכים לא תקינים. */
export function sanitizeSettings(input, old = defaultSettings()) {
  const i = input || {};
  const d = defaultSettings();
  const s = { ...d, ...old };
  if ("rewardTitle" in i) s.rewardTitle = str(i.rewardTitle, 50, d.rewardTitle);
  if ("rewardCost" in i) s.rewardCost = clamp(i.rewardCost, 1, 10_000_000, s.rewardCost);
  if ("rewardDescription" in i) s.rewardDescription = typeof i.rewardDescription === "string" ? i.rewardDescription.trim().slice(0, 200) : s.rewardDescription;
  if ("voice" in i && VOICES[i.voice]) s.voice = i.voice;
  if ("playback" in i && ["edge", "server", "gemini"].includes(i.playback)) s.playback = i.playback;
  if ("geminiVoice" in i && GEMINI_VOICES[i.geminiVoice]) s.geminiVoice = i.geminiVoice;
  if ("rate" in i) s.rate = clamp(i.rate, -50, 50, s.rate);
  if ("volume" in i) s.volume = clamp(i.volume, 0, 100, s.volume);
  if ("template" in i) s.template = typeof i.template === "string" && i.template.includes("{text}") ? i.template.trim().slice(0, 100) : d.template;
  if ("maxChars" in i) s.maxChars = clamp(i.maxChars, 10, 400, s.maxChars);
  if ("blockLinks" in i) s.blockLinks = !!i.blockLinks;
  if ("manualApproval" in i) s.manualApproval = !!i.manualApproval;
  if ("maxAgeMinutes" in i) s.maxAgeMinutes = clamp(i.maxAgeMinutes, 1, 1440, s.maxAgeMinutes);
  if ("gapSeconds" in i) s.gapSeconds = clamp(i.gapSeconds, 0, 60, s.gapSeconds);
  if ("chatEnabled" in i) s.chatEnabled = !!i.chatEnabled;
  if ("chatPrefix" in i) s.chatPrefix = validPrefix(i.chatPrefix) ? i.chatPrefix.trim() : (validPrefix(s.chatPrefix) ? s.chatPrefix : d.chatPrefix);
  if ("chatWho" in i && ["all", "subs", "vip", "mods"].includes(i.chatWho)) s.chatWho = i.chatWho;
  if ("chatCooldown" in i) s.chatCooldown = clamp(i.chatCooldown, 0, 3600, s.chatCooldown);
  for (const k of ALERT_KINDS) {
    const K = ALERT_KEY[k];
    if (("alert" + K) in i) s["alert" + K] = !!i["alert" + K];
    if (("alert" + K + "Template") in i) {
      const v = typeof i["alert" + K + "Template"] === "string" ? i["alert" + K + "Template"].trim().slice(0, 120) : "";
      s["alert" + K + "Template"] = v.includes("{user}") || v.includes("{text}") ? v : d["alert" + K + "Template"];
    }
  }
  if ("alertShowCard" in i) s.alertShowCard = !!i.alertShowCard;
  if ("alertKicksMin" in i) s.alertKicksMin = clamp(i.alertKicksMin, 1, 1_000_000, s.alertKicksMin);
  if ("banned" in i && Array.isArray(i.banned)) {
    s.banned = [...new Set(i.banned.filter((w) => typeof w === "string").map((w) => w.trim()).filter(Boolean))].slice(0, 500);
  }
  return s;
}

/** הסימן חייב להתחיל בתו שאינו אות/ספרה (כדי ששאר הצ'אט לא יגיע לשרת בכלל). */
export const validPrefix = (p) => typeof p === "string" && /^[^\p{L}\p{N}\s]\S{0,9}$/u.test(p.trim());

/** האם ההודעה היא פקודת הקראה – מחזיר את הטקסט (בלי הסימן) או null. */
export function chatCommandText(content, prefix) {
  const c = String(content || "").trim();
  const p = String(prefix || "").trim();
  if (!p || c.length <= p.length || c.slice(0, p.length).toLowerCase() !== p.toLowerCase()) return null;
  const rest = c.slice(p.length);
  // "!tts" לא תופס את "!ttsx", אבל "!" תופס את "!שלום"
  if (/[\p{L}\p{N}]$/u.test(p) && !/^\s/.test(rest)) return null;
  const text = rest.trim();
  return text || null;
}

/** מי רשאי להשתמש בפקודה לפי ההגדרה ותגי המשתמש. */
export function chatAllowed(who, badges) {
  const b = new Set(badges);
  const mod = b.has("broadcaster") || b.has("moderator");
  if (who === "mods") return mod;
  const vip = mod || b.has("vip");
  if (who === "vip") return vip;
  if (who === "subs") return vip || b.has("subscriber") || b.has("founder") || b.has("og");
  return true;
}

const EMOTE = /\[emote:\d+:[^\]]*\]/g;
const LINK = /(https?:\/\/|www\.)\S+|\b[\p{L}\p{N}-]+\.(com|net|org|io|gg|tv|co|me|ly|xyz|ru|il|app|link|site|live)\b(\/\S*)?/iu;

const FINALS = { "ך": "כ", "ם": "מ", "ן": "נ", "ף": "פ", "ץ": "צ" };
function normalize(s) {
  return s.normalize("NFD").replace(/\p{Mn}/gu, "").replace(/[ךםןףץ]/g, (c) => FINALS[c]).toLowerCase();
}
const escapeRe = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

/** מילה שלמה, כולל אותיות שימוש בעברית לפניה (ו/ה/ש/ב/כ/ל/מ). */
export function containsWord(text, word) {
  const w = normalize(word.trim());
  if (!w) return false;
  const re = new RegExp(`(?<![\\p{L}\\p{N}])[והשבכלמ]{0,2}${escapeRe(w).replace(/ /g, "\\s+")}(?![\\p{L}\\p{N}])`, "u");
  return re.test(normalize(text));
}

/** בודק ומנקה הודעה. {ok, text, reason} */
export function checkMessage(input, s) {
  let text = String(input ?? "").replace(EMOTE, " ").replace(/\p{Cc}/gu, "");
  if (s.blockLinks && LINK.test(text)) return { ok: false, text, reason: "ההודעה מכילה קישור" };
  text = text.replace(/(.)\1{3,}/gu, "$1$1$1").replace(/\s+/g, " ").trim();
  if (!/[\p{L}\p{N}]/u.test(text)) return { ok: false, text, reason: "ההודעה ריקה" };
  for (const w of s.banned || []) if (containsWord(text, w)) return { ok: false, text, reason: "ההודעה מכילה מילה חסומה" };
  if (text.length > s.maxChars) text = text.slice(0, s.maxChars).trimEnd() + "…";
  return { ok: true, text, reason: null };
}

export const applyTemplate = (template, user, text) => template.replace("{user}", user).replace("{text}", text).trim();

const escapeXml = (s) => s.replace(/[<>&'"]/g, (c) => ({ "<": "&lt;", ">": "&gt;", "&": "&amp;", "'": "&apos;", '"': "&quot;" })[c]);

/** מחזיר MP3 (ArrayBuffer). זורק Error עם הודעה בעברית. */
export async function synthesize(env, text, voice, rate) {
  if (!env.AZURE_SPEECH_KEY || !env.AZURE_SPEECH_REGION) throw new Error("הקולות עוד לא הופעלו בשרת (חסר מפתח Azure).");
  if (!VOICES[voice]) voice = "he-IL-HilaNeural";
  const ssml =
    `<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="he-IL">` +
    `<voice name="${voice}"><prosody rate="${rate >= 0 ? "+" : ""}${rate}%">${escapeXml(text.slice(0, 600))}</prosody></voice></speak>`;
  let res;
  try {
    res = await fetch(`https://${env.AZURE_SPEECH_REGION}.tts.speech.microsoft.com/cognitiveservices/v1`, {
      method: "POST",
      headers: {
        "Ocp-Apim-Subscription-Key": env.AZURE_SPEECH_KEY,
        "Content-Type": "application/ssml+xml",
        "X-Microsoft-OutputFormat": "audio-24khz-48kbitrate-mono-mp3",
        "User-Agent": "catego-web",
      },
      body: ssml,
    });
  } catch {
    throw new Error("אין חיבור לשירות הקולות.");
  }
  if (res.status === 429) throw new Error("המכסה החודשית של הקולות נגמרה.");
  if (!res.ok) throw new Error(`שירות הקולות החזיר שגיאה ${res.status}.`);
  return res.arrayBuffer();
}

// ───────────────────────── Gemini (Google AI Studio, יש מסלול חינמי) ─────────────────────────

/** מוצא בתשובה את קובץ השמע (base64) – בלי להניח מבנה מדויק מדי. */
function findAudio(node, depth = 0) {
  if (!node || typeof node !== "object" || depth > 8) return null;
  const mime = String(node.mime_type || node.mimeType || "");
  if (typeof node.data === "string" && node.data.length > 200 && (mime.startsWith("audio") || node.type === "audio")) {
    return { data: node.data, mime: mime || "audio/wav" };
  }
  for (const v of Array.isArray(node) ? node : Object.values(node)) {
    const hit = findAudio(v, depth + 1);
    if (hit) return hit;
  }
  return null;
}

/** עוטף PCM של 16 ביט (24kHz מונו) בכותרת WAV, כדי שהדפדפן ידע לנגן. */
export function pcmToWav(pcm, rate = 24000) {
  const out = new Uint8Array(44 + pcm.length);
  const v = new DataView(out.buffer);
  const w = (o, s) => [...s].forEach((c, i) => v.setUint8(o + i, c.charCodeAt(0)));
  w(0, "RIFF"); v.setUint32(4, 36 + pcm.length, true); w(8, "WAVE"); w(12, "fmt ");
  v.setUint32(16, 16, true); v.setUint16(20, 1, true); v.setUint16(22, 1, true);
  v.setUint32(24, rate, true); v.setUint32(28, rate * 2, true); v.setUint16(32, 2, true); v.setUint16(34, 16, true);
  w(36, "data"); v.setUint32(40, pcm.length, true);
  out.set(pcm, 44);
  return out;
}

/** מחזיר {data: Uint8Array (WAV), type: "audio/wav"}. זורק Error בעברית. */
export async function synthesizeGemini(env, text, voice, rate) {
  if (!env.GEMINI_API_KEY) throw new Error("Gemini עוד לא הוגדר בשרת (חסר מפתח).");
  if (!GEMINI_VOICES[voice]) voice = "Kore";
  const style = rate <= -15 ? "speaking slowly and clearly" : rate >= 15 ? "speaking quickly and energetically" : "natural, friendly";
  let res;
  try {
    res = await fetch("https://generativelanguage.googleapis.com/v1beta/interactions", {
      method: "POST",
      headers: { "Content-Type": "application/json", "x-goog-api-key": env.GEMINI_API_KEY },
      body: JSON.stringify({
        model: env.GEMINI_TTS_MODEL || GEMINI_MODEL,
        input: [{ type: "user_input", content: [{ type: "text", text: text.slice(0, 600), annotations: [{ type: "speech_metadata", style }] }] }],
        response_format: { type: "audio", mime_type: "audio/wav", sample_rate: 24000 },
        generation_config: { speech_config: [{ voice }] },
      }),
    });
  } catch {
    throw new Error("אין חיבור ל-Gemini.");
  }
  if (res.status === 429) throw new Error("הגענו למגבלה היומית/לדקה של Gemini החינמי.");
  if (res.status === 400 || res.status === 403) throw new Error("Gemini דחה את הבקשה (בדוק את המפתח).");
  if (!res.ok) throw new Error(`Gemini החזיר שגיאה ${res.status}.`);
  const audio = findAudio(await res.json().catch(() => null));
  if (!audio) throw new Error("Gemini לא החזיר שמע.");
  const bytes = Uint8Array.from(atob(audio.data), (c) => c.charCodeAt(0));
  const isWav = bytes[0] === 0x52 && bytes[1] === 0x49 && bytes[2] === 0x46 && bytes[3] === 0x46; // "RIFF"
  return { data: isWav ? bytes : pcmToWav(bytes), type: "audio/wav" };
}
