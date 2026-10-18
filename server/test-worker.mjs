// Local test for worker.js (Node 18+): node server/test-worker.mjs
import worker from "./worker.js";

const env = { KICK_CLIENT_ID: "cid", KICK_CLIENT_SECRET: "super-secret" };
let lastKickCall = null;

globalThis.fetch = async (url, init) => {
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

console.log(failed ? `\n${failed} FAILED` : "\nAll worker tests passed");
process.exit(failed ? 1 : 0);
