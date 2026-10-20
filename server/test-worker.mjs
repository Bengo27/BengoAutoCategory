// Local test for worker.js (Node 18+): node server/test-worker.mjs
import worker from "./worker.js";

const env = { KICK_CLIENT_ID: "cid", KICK_CLIENT_SECRET: "super-secret" };
let lastKickCall = null;

globalThis.fetch = async (url, init) => {
  if (String(url).includes("tts.speech.microsoft.com")) {
    lastKickCall = { url, init, form: new URLSearchParams() };
    if (init.headers["Ocp-Apim-Subscription-Key"] !== "az-key") return new Response("", { status: 401 });
    if (init.body.includes("QUOTA")) return new Response("", { status: 429 });
    return new Response(new Uint8Array([0x49, 0x44, 0x33]), { status: 200, headers: { "Content-Type": "audio/mpeg" } });
  }
  lastKickCall = { url, init, form: new URLSearchParams(init.body) };
  const f = lastKickCall.form;
  if (f.get("client_secret") !== "super-secret") return new Response('{"error":"invalid_client"}', { status: 401 });
  if (f.get("grant_type") === "refresh_token" && f.get("refresh_token") !== "r1")
    return new Response('{"error":"invalid_grant"}', { status: 400, headers: { "Content-Type": "application/json" } });
  return new Response('{"access_token":"a2","refresh_token":"r2","expires_in":3600}', { status: 200, headers: { "Content-Type": "application/json" } });
};

const req = (path, method = "POST", body) =>
  new Request("https://broker.example" + path, { method, body: body === undefined ? undefined : JSON.stringify(body) });

let failed = 0;
const check = (cond, name) => { console.log((cond ? "PASS  " : "FAIL  ") + name); if (!cond) failed++; };

let r = await worker.fetch(req("/health", "GET"), env);
check(r.status === 200 && (await r.json()).client_id === "cid", "health");

r = await worker.fetch(req("/token", "POST", { code: "c", code_verifier: "v", redirect_uri: "http://localhost:47653/callback" }), env);
const body = await r.text();
check(r.status === 200 && body.includes('"access_token":"a2"'), "token exchange");
check(lastKickCall.url === "https://id.kick.com/oauth/token", "calls Kick token URL");
check(lastKickCall.form.get("grant_type") === "authorization_code" && lastKickCall.form.get("code_verifier") === "v", "auth code + PKCE forwarded");
check(!body.includes("super-secret"), "secret never returned");

lastKickCall = null;
r = await worker.fetch(req("/token", "POST", { code: "c", code_verifier: "v", redirect_uri: "https://evil.example/cb" }), env);
check(r.status === 400 && lastKickCall === null, "foreign redirect_uri rejected");

r = await worker.fetch(req("/refresh", "POST", { refresh_token: "r1" }), env);
check(r.status === 200 && lastKickCall.form.get("grant_type") === "refresh_token", "refresh");

r = await worker.fetch(req("/refresh", "POST", { refresh_token: "bad" }), env);
check(r.status === 400, "bad refresh passes Kick error through");

r = await worker.fetch(req("/token", "POST", {}), env);
check(r.status === 400, "missing fields");

r = await worker.fetch(req("/token", "GET"), env);
check(r.status === 404, "GET /token not allowed");

r = await worker.fetch(req("/token", "POST", { code: "c", code_verifier: "v", redirect_uri: "http://localhost:47653/callback" }), {});
check(r.status === 500, "not configured");

// ── TTS ──
const ttsEnv = { ...env, AZURE_SPEECH_KEY: "az-key", AZURE_SPEECH_REGION: "westeurope" };
r = await worker.fetch(req("/health", "GET"), ttsEnv);
check((await r.json()).tts === true, "health reports tts");

r = await worker.fetch(req("/tts", "POST", { text: "שלום <עולם> & חברים", voice: "he-IL-HilaNeural", rate: 10 }), ttsEnv);
check(r.status === 200 && r.headers.get("Content-Type") === "audio/mpeg", "tts returns mp3");
check(lastKickCall.url === "https://westeurope.tts.speech.microsoft.com/cognitiveservices/v1", "tts calls Azure region");
check(lastKickCall.init.body.includes("&lt;עולם&gt; &amp;") && lastKickCall.init.body.includes('rate="+10%"'), "tts escapes SSML + rate");

r = await worker.fetch(req("/tts", "POST", { text: "hi", voice: "en-US-JennyNeural" }), ttsEnv);
check(r.status === 400, "tts rejects other voices");

r = await worker.fetch(req("/tts", "POST", { text: "x".repeat(401), voice: "he-IL-AvriNeural" }), ttsEnv);
check(r.status === 400, "tts rejects long text");

r = await worker.fetch(req("/tts", "POST", { text: "QUOTA", voice: "he-IL-AvriNeural" }), ttsEnv);
check(r.status === 429, "tts quota passes 429");

r = await worker.fetch(req("/tts", "POST", { text: "hi", voice: "he-IL-AvriNeural" }), env);
check(r.status === 503, "tts not configured");

console.log(failed ? `\n${failed} FAILED` : "\nAll worker tests passed");
process.exit(failed ? 1 : 0);
