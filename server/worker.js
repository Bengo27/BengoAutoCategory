/**
 * Catego – Kick token broker (Cloudflare Worker)
 *
 * Holds the Kick app's Client Secret so streamers never need their own Kick developer app.
 * It only does the two OAuth calls that require the secret:
 *   POST /token    { code, code_verifier, redirect_uri }  -> Kick authorization_code exchange
 *   POST /refresh  { refresh_token }                      -> Kick refresh_token grant
 *   GET  /health                                          -> { ok, client_id }
 *
 * Required secrets (Cloudflare dashboard -> Worker -> Settings -> Variables and Secrets):
 *   KICK_CLIENT_ID      the app's Client ID
 *   KICK_CLIENT_SECRET  the app's Client Secret   (type: Secret)
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

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method === "GET" && (url.pathname === "/" || url.pathname === "/health")) {
      return json(200, { ok: true, service: "bengo-autocategory-broker", client_id: env.KICK_CLIENT_ID || null });
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

function isStr(v) {
  return typeof v === "string" && v.length > 0 && v.length < 4096;
}

function json(status, obj) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: { "Content-Type": "application/json", "Cache-Control": "no-store" },
  });
}
