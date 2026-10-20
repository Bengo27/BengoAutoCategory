/**
 * Catego – Kick token broker (Cloudflare Worker)
 *
 * Holds the Kick app's Client Secret so streamers never need their own Kick developer app.
 * It only does the two OAuth calls that require the secret:
 *   POST /token    { code, code_verifier, redirect_uri }  -> Kick authorization_code exchange
 *   POST /refresh  { refresh_token }                      -> Kick refresh_token grant
 *   POST /tts      { text, voice, rate }                  -> MP3 audio (Azure neural voice, Hebrew)
 *   GET  /health                                          -> { ok, client_id, tts }
 *   GET  /download/studio                                 -> redirect to the newest CategoStudio.exe
 *
 * Required secrets (Cloudflare dashboard -> Worker -> Settings -> Variables and Secrets):
 *   KICK_CLIENT_ID      the app's Client ID
 *   KICK_CLIENT_SECRET  the app's Client Secret   (type: Secret)
 * Optional (text-to-speech with the Hila / Avri voices):
 *   AZURE_SPEECH_KEY     Azure "Speech" resource key (free F0 tier)   (type: Secret)
 *   AZURE_SPEECH_REGION  e.g. westeurope
 *
 * The streamer still logs in on Kick's own page (OAuth + PKCE). The broker never sees passwords,
 * and it does not store tokens – it only forwards Kick's response back to the app.
 */

const KICK_TOKEN_URL = "https://id.kick.com/oauth/token";

// Only the desktop app's local callback is accepted.
const ALLOWED_REDIRECTS = new Set([
  "http://localhost:47653/callback",
]);

const MAX_BODY_BYTES = 8 * 1024;

// TTS: only these voices, and short texts (protects the free Azure quota).
const TTS_VOICES = new Set(["he-IL-HilaNeural", "he-IL-AvriNeural"]);
const TTS_MAX_CHARS = 400;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method === "GET" && (url.pathname === "/" || url.pathname === "/health")) {
      return json(200, {
        ok: true,
        service: "bengo-autocategory-broker",
        client_id: env.KICK_CLIENT_ID || null,
        tts: Boolean(env.AZURE_SPEECH_KEY && env.AZURE_SPEECH_REGION),
      });
    }

    if (request.method === "GET" && url.pathname === "/download/studio") {
      return downloadStudio(env);
    }

    if (request.method === "POST" && url.pathname === "/tts") {
      return tts(request, env);
    }

    if (request.method !== "POST" || (url.pathname !== "/token" && url.pathname !== "/refresh")) {
      return json(404, { error: "not_found" });
    }

    if (!env.KICK_CLIENT_ID || !env.KICK_CLIENT_SECRET) {
      return json(500, { error: "server_not_configured", error_description: "KICK_CLIENT_ID / KICK_CLIENT_SECRET are not set" });
    }

    let body;
    try {
      const text = await request.text();
      if (text.length > MAX_BODY_BYTES) return json(413, { error: "too_large" });
      body = JSON.parse(text || "{}");
    } catch {
      return json(400, { error: "invalid_json" });
    }

    const form = new URLSearchParams();
    form.set("client_id", env.KICK_CLIENT_ID);
    form.set("client_secret", env.KICK_CLIENT_SECRET);

    if (url.pathname === "/token") {
      const { code, code_verifier, redirect_uri } = body;
      if (!isStr(code) || !isStr(code_verifier) || !isStr(redirect_uri)) {
        return json(400, { error: "invalid_request", error_description: "code, code_verifier and redirect_uri are required" });
      }
      if (!ALLOWED_REDIRECTS.has(redirect_uri)) {
        return json(400, { error: "invalid_redirect_uri" });
      }
      form.set("grant_type", "authorization_code");
      form.set("code", code);
      form.set("code_verifier", code_verifier);
      form.set("redirect_uri", redirect_uri);
    } else {
      const { refresh_token } = body;
      if (!isStr(refresh_token)) {
        return json(400, { error: "invalid_request", error_description: "refresh_token is required" });
      }
      form.set("grant_type", "refresh_token");
      form.set("refresh_token", refresh_token);
    }

    let kick;
    try {
      kick = await fetch(KICK_TOKEN_URL, {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded", "Accept": "application/json" },
        body: form.toString(),
      });
    } catch (e) {
      return json(502, { error: "kick_unreachable" });
    }

    // Pass Kick's answer through unchanged (status + body). Never echo the secret.
    const text = await kick.text();
    return new Response(text, {
      status: kick.status,
      headers: { "Content-Type": kick.headers.get("Content-Type") || "application/json", "Cache-Control": "no-store" },
    });
  },
};

// Catego Studio releases are tagged studio-v* and are never "Latest" (that stays Catego),
// so this gives streamers one permanent download link.
async function downloadStudio(env) {
  const repo = env.GITHUB_REPO || "Bengo27/BengoAutoCategory";
  let res;
  try {
    res = await fetch(`https://api.github.com/repos/${repo}/releases?per_page=30`, {
      headers: { "User-Agent": "catego-broker", "Accept": "application/vnd.github+json" },
    });
  } catch {
    return json(502, { error: "github_unreachable" });
  }
  if (!res.ok) return json(502, { error: "github_failed", status: res.status });
  const releases = await res.json();
  for (const r of releases) {
    if (r.draft || r.prerelease || !String(r.tag_name || "").startsWith("studio-v")) continue;
    const asset = (r.assets || []).find((a) => a.name === "CategoStudio.exe");
    if (asset) return Response.redirect(asset.browser_download_url, 302);
  }
  return json(404, { error: "no_studio_release" });
}

async function tts(request, env) {
  if (!env.AZURE_SPEECH_KEY || !env.AZURE_SPEECH_REGION) {
    return json(503, { error: "tts_not_configured" });
  }
  let body;
  try {
    const raw = await request.text();
    if (raw.length > MAX_BODY_BYTES) return json(413, { error: "too_large" });
    body = JSON.parse(raw || "{}");
  } catch {
    return json(400, { error: "invalid_json" });
  }
  const text = typeof body.text === "string" ? body.text.trim() : "";
  const voice = body.voice;
  const rate = Number.isInteger(body.rate) ? Math.max(-50, Math.min(50, body.rate)) : 0;
  if (!text || text.length > TTS_MAX_CHARS) return json(400, { error: "invalid_text" });
  if (!TTS_VOICES.has(voice)) return json(400, { error: "invalid_voice" });

  const ssml =
    `<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="he-IL">` +
    `<voice name="${voice}"><prosody rate="${rate >= 0 ? "+" : ""}${rate}%">${escapeXml(text)}</prosody></voice></speak>`;

  let res;
  try {
    res = await fetch(`https://${env.AZURE_SPEECH_REGION}.tts.speech.microsoft.com/cognitiveservices/v1`, {
      method: "POST",
      headers: {
        "Ocp-Apim-Subscription-Key": env.AZURE_SPEECH_KEY,
        "Content-Type": "application/ssml+xml",
        "X-Microsoft-OutputFormat": "audio-24khz-48kbitrate-mono-mp3",
        "User-Agent": "catego-broker",
      },
      body: ssml,
    });
  } catch {
    return json(502, { error: "tts_unreachable" });
  }
  if (!res.ok) {
    // 429 = the free monthly quota is used up (the app falls back to the Windows voice)
    return json(res.status === 429 ? 429 : 502, { error: "tts_failed", status: res.status });
  }
  return new Response(res.body, {
    status: 200,
    headers: { "Content-Type": "audio/mpeg", "Cache-Control": "no-store" },
  });
}

function escapeXml(s) {
  return s.replace(/[<>&'"]/g, (c) => ({ "<": "&lt;", ">": "&gt;", "&": "&amp;", "'": "&apos;", '"': "&quot;" })[c]);
}

function isStr(v) {
  return typeof v === "string" && v.length > 0 && v.length < 4096;
}

function json(status, obj) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: { "Content-Type": "application/json", "Cache-Control": "no-store" },
  });
}
