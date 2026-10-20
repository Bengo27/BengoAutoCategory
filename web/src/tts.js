// סינון הודעות + הקראה דרך Azure (אותם כללים כמו בתוכנה)
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
    playback: "edge",
    rate: 0,
    volume: 80,
    template: "{user} אומר: {text}",
    maxChars: 200,
    blockLinks: true,
    manualApproval: false,
    maxAgeMinutes: 10,
    gapSeconds: 2,
    banned: [...DEFAULT_BANNED],
  };
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
  if ("playback" in i && (i.playback === "edge" || i.playback === "server")) s.playback = i.playback;
  if ("rate" in i) s.rate = clamp(i.rate, -50, 50, s.rate);
  if ("volume" in i) s.volume = clamp(i.volume, 0, 100, s.volume);
  if ("template" in i) s.template = typeof i.template === "string" && i.template.includes("{text}") ? i.template.trim().slice(0, 100) : d.template;
  if ("maxChars" in i) s.maxChars = clamp(i.maxChars, 10, 400, s.maxChars);
  if ("blockLinks" in i) s.blockLinks = !!i.blockLinks;
  if ("manualApproval" in i) s.manualApproval = !!i.manualApproval;
  if ("maxAgeMinutes" in i) s.maxAgeMinutes = clamp(i.maxAgeMinutes, 1, 1440, s.maxAgeMinutes);
  if ("gapSeconds" in i) s.gapSeconds = clamp(i.gapSeconds, 0, 60, s.gapSeconds);
  if ("banned" in i && Array.isArray(i.banned)) {
    s.banned = [...new Set(i.banned.filter((w) => typeof w === "string").map((w) => w.trim()).filter(Boolean))].slice(0, 500);
  }
  return s;
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
