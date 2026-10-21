// Now Playing – השיר שמתנגן בספוטיפיי, לפי הפרויקט המקורי של Bengo (7 עיצובים).
// הסטרימר מחבר אפליקציית Spotify משלו (Client ID, בלי Secret – PKCE).
// הווידג'ט ב-OBS שואל את ספוטיפיי ישירות (כל 4 שניות) – כך השרת שלנו כמעט לא עובד.

export const SPOTIFY = {
  authorize: "https://accounts.spotify.com/authorize",
  token: "https://accounts.spotify.com/api/token",
  scope: "user-read-currently-playing",
};

export const NP_THEMES = ["classic", "minimal", "glass", "neon", "album", "slim", "vinyl"];

export function npDefaults() {
  return { theme: "classic", accent: "#a6ef67", opacity: 0.94, scale: 1, hidePaused: true, compact: false };
}

export function sanitizeNp(input, old = npDefaults()) {
  const i = input || {};
  const s = { ...npDefaults(), ...old };
  if (NP_THEMES.includes(i.theme)) s.theme = i.theme;
  if (typeof i.accent === "string" && /^#[0-9a-f]{6}$/i.test(i.accent)) s.accent = i.accent;
  if (Number.isFinite(Number(i.opacity))) s.opacity = Math.min(1, Math.max(0.15, Number(i.opacity)));
  if (Number.isFinite(Number(i.scale))) s.scale = Math.min(1.3, Math.max(0.6, Number(i.scale)));
  if (typeof i.hidePaused === "boolean") s.hidePaused = i.hidePaused;
  if (typeof i.compact === "boolean") s.compact = i.compact;
  return s;
}

export const validClientId = (id) => typeof id === "string" && /^[0-9a-f]{32}$/i.test(id.trim());

async function tokenRequest(params) {
  let res;
  try {
    res = await fetch(SPOTIFY.token, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams(params).toString(),
    });
  } catch {
    const e = new Error("אין חיבור לספוטיפיי."); e.status = 502; throw e;
  }
  const j = await res.json().catch(() => ({}));
  if (!res.ok || !j.access_token) {
    const e = new Error(j.error === "invalid_grant" ? "החיבור לספוטיפיי פג – התחבר מחדש." : "ספוטיפיי דחה את ההתחברות.");
    e.status = res.status; e.code = j.error; throw e;
  }
  return { access: j.access_token, refresh: j.refresh_token || "", expires: Date.now() + (Number(j.expires_in) || 3600) * 1000 };
}

export const spotifyExchange = (clientId, code, verifier, redirect) =>
  tokenRequest({ grant_type: "authorization_code", code, redirect_uri: redirect, client_id: clientId, code_verifier: verifier });

export const spotifyRefresh = (clientId, refresh) =>
  tokenRequest({ grant_type: "refresh_token", refresh_token: refresh, client_id: clientId });

// ───────────────────────── הווידג'ט ל-OBS (העיצובים של Bengo) ─────────────────────────

const NP_CSS = `
*{box-sizing:border-box}html,body{margin:0;background:transparent!important;overflow:hidden;font-family:Arial,sans-serif;color:#fff}
#card{--accent:#a6ef67;position:absolute;left:18px;top:18px;width:460px;padding:18px;display:flex;gap:18px;border:1px solid #ffffff25;border-radius:22px;background:rgba(17,20,23,.94);box-shadow:0 10px 28px #0004;transform-origin:top left;transition:opacity .5s,width .6s;opacity:0}
#art{width:96px;height:96px;flex-shrink:0;display:grid;place-items:center;background:linear-gradient(135deg,#354f36,#1b262b);font-size:44px;border-radius:9px;overflow:hidden}
#art img{width:100%;height:100%;object-fit:contain}#info{min-width:0;flex:1}
.label{font-size:10px;letter-spacing:2px;color:var(--accent);display:flex;justify-content:space-between;margin-bottom:11px}
#titleBox,#artistBox{overflow:hidden;white-space:nowrap}#title{font-size:23px;font-weight:700;display:inline-block}#artist{font-size:14px;color:#aab2b9;display:inline-block;margin-top:7px}
.scroll{animation:marquee 12s ease-in-out infinite alternate}@keyframes marquee{0%,15%{transform:translateX(0)}85%,100%{transform:translateX(var(--travel))}}
.bar{height:3px;background:#ffffff20;margin-top:16px;border-radius:5px;overflow:hidden}#fill{height:100%;width:0;background:var(--accent)}
.times{display:flex;justify-content:space-between;color:#78868d;font-size:10px;margin-top:6px}
#card.compact{width:380px;padding:12px;gap:12px}#card.compact #art{width:62px;height:62px}#card.compact #title{font-size:18px}#card.compact .bar,#card.compact .times{display:none}#card.compact .label{margin-bottom:7px}
#badge{letter-spacing:0;color:#93a19a}
#card[data-theme="minimal"]{border:0;box-shadow:none;border-radius:0;background:transparent!important;padding:10px;text-shadow:0 2px 6px #000}
#card[data-theme="minimal"] .bar{margin-top:12px}#card[data-theme="minimal"] #art{width:80px;height:80px}
#card[data-theme="glass"]{background:linear-gradient(120deg,rgba(135,158,174,var(--bg-alpha)),rgba(32,39,52,var(--bg-alpha)))!important;border:1px solid #ffffff60;box-shadow:inset 0 1px 0 #ffffff55,0 10px 28px #0004}
#card[data-theme="neon"]{border-color:var(--accent);border-radius:12px;box-shadow:0 0 16px color-mix(in srgb,var(--accent) 40%,transparent),inset 0 0 20px color-mix(in srgb,var(--accent) 10%,transparent)}
#card[data-theme="neon"] .label{text-shadow:0 0 12px var(--accent)}
#card[data-theme="album"]{width:290px;display:flex;flex-direction:column;gap:14px;padding:16px;border-radius:18px}
#card[data-theme="album"] #art{width:256px;height:256px;border-radius:8px}#card[data-theme="album"] #title{font-size:22px}
#card[data-theme="album"].compact{width:380px;flex-direction:row;padding:12px;gap:12px}#card[data-theme="album"].compact #art{width:62px;height:62px}
#card[data-theme="slim"]{width:490px;padding:10px 14px;gap:12px;border-radius:12px;align-items:center}
#card[data-theme="slim"] #art{width:58px;height:58px}#card[data-theme="slim"] #title{font-size:19px}#card[data-theme="slim"] .label{margin-bottom:5px}#card[data-theme="slim"] #artist{margin-top:4px;font-size:12px}#card[data-theme="slim"] .bar{margin-top:8px}#card[data-theme="slim"] .times{display:none}
#card[data-theme="vinyl"] #art{width:108px;height:108px;border-radius:50%;position:relative;background:repeating-radial-gradient(#16191b 0 3px,#303536 4px 5px);animation:spin 9s linear infinite;animation-play-state:paused}
#card[data-theme="vinyl"].playing #art{animation-play-state:running}#card[data-theme="vinyl"] #art img{width:60%;height:60%;border-radius:50%;position:absolute;left:20%;top:20%}
#card[data-theme="vinyl"] #art::after{content:'';position:absolute;width:9px;height:9px;border-radius:50%;background:#14191c;left:calc(50% - 4.5px);top:calc(50% - 4.5px);border:1px solid #ffffff50}
#card[data-theme="vinyl"].compact #art{width:62px;height:62px}@keyframes spin{to{transform:rotate(360deg)}}
@media(prefers-reduced-motion:reduce){.scroll,#card[data-theme="vinyl"] #art{animation:none!important}}
`;

const NP_JS = String.raw`
const $=id=>document.getElementById(id);
const params=new URLSearchParams(location.search);const demo=params.has("demo");const base=location.pathname.replace(/\/$/,"");
let track=null,lastId="",changedAt=0,config={theme:"classic",accent:"#a6ef67",opacity:.94,scale:1,hidePaused:true,compact:false},lastSuccess=0;
let token=null,tokenExp=0,ws=null,retry=1000,pollTimer=null,waitingToken=false;
if(params.get("theme"))config.theme=params.get("theme");
if(demo){const a=params.get("accent"),o=+params.get("opacity"),s=+params.get("scale");if(/^#[0-9a-f]{6}$/i.test(a||""))config.accent=a;
 if(o>=.15&&o<=1)config.opacity=o;if(s>=.6&&s<=1.3)config.scale=s;if(params.get("compact")==="1")config.compact=true;}
const format=ms=>Math.floor(ms/60000)+":"+String(Math.floor(ms/1000)%60).padStart(2,"0");
function marquee(id,box){const el=$(id),extra=el.scrollWidth-$(box).clientWidth;el.classList.toggle("scroll",extra>0);el.style.setProperty("--travel",(-Math.max(0,extra))+"px");}
function applyConfig(){const card=$("card");card.dataset.theme=config.theme||"classic";card.style.setProperty("--bg-alpha",config.opacity);card.style.setProperty("--accent",config.accent);
 card.style.background="rgba(17,20,23,"+config.opacity+")";card.style.transform="scale("+config.scale+")";}
function show(t,isDemo){track=t;lastSuccess=Date.now();const card=$("card");card.classList.toggle("playing",!!t&&t.playing);
 if(!t)return;if(lastId!==t.id){lastId=t.id;changedAt=Date.now();$("title").textContent=t.title;$("artist").textContent=t.artist;$("art").replaceChildren();
  if(t.image&&/^https:\/\//.test(t.image)){const img=new Image();img.alt="";img.src=t.image;img.onerror=()=>{$("art").textContent="♫";};$("art").append(img);}else $("art").textContent="♫";}
 $("state").textContent=isDemo?"DEMO • NOW PLAYING":t.playing?"NOW PLAYING":"PAUSED";$("badge").textContent=isDemo?"Catego":"Spotify";$("duration").textContent=format(t.duration);}
function normalize(p){const t=p&&p.item;if(!t||p.currently_playing_type==="ad")return null;
 return{id:t.id||t.uri||t.name,title:t.name,artist:(t.artists||[]).map(a=>a.name).join(", ")||(t.show&&t.show.publisher)||"",
  image:(t.album&&t.album.images&&t.album.images[0]&&t.album.images[0].url)||(t.images&&t.images[0]&&t.images[0].url)||"",duration:t.duration_ms||0,progress:p.progress_ms||0,playing:!!p.is_playing,at:Date.now()};}
function askToken(){if(waitingToken||!ws||ws.readyState!==1)return;waitingToken=true;ws.send(JSON.stringify({type:"token"}));}
async function poll(){clearTimeout(pollTimer);let delay=4000;
 try{if(!token||Date.now()>tokenExp-30000){askToken();delay=2000;}
  else{const r=await fetch("https://api.spotify.com/v1/me/player/currently-playing?additional_types=episode",{headers:{Authorization:"Bearer "+token}});
   if(r.status===204){show(null);track=null;}else if(r.status===401){token=null;askToken();delay=1500;}
   else if(r.status===429){delay=Math.max(5000,Number(r.headers.get("retry-after")||30)*1000);}
   else if(r.ok){show(normalize(await r.json()));}else delay=15000;}}catch(e){delay=10000;}
 pollTimer=setTimeout(poll,delay);}
function connect(){ws=new WebSocket((location.protocol==="https:"?"wss://":"ws://")+location.host+base+"/ws");
 ws.onopen=()=>{retry=1000;waitingToken=false;askToken();};
 ws.onmessage=e=>{let m;try{m=JSON.parse(e.data);}catch{return;}
  if(m.type==="config"){config=Object.assign(config,m.settings||{});applyConfig();}
  else if(m.type==="token"){waitingToken=false;token=m.access||null;tokenExp=m.expires||0;if(token)poll();}};
 ws.onclose=e=>{waitingToken=false;if(e.code===4003)return;setTimeout(connect,retry);retry=Math.min(retry*2,15000);};}
setInterval(()=>{const card=$("card");const visible=track&&(demo||Date.now()-lastSuccess<15000)&&(track.playing||!config.hidePaused);
 card.style.opacity=visible?"1":"0";card.classList.toggle("compact",!!config.compact&&Date.now()-changedAt>5000);
 if(track){const p=Math.min(track.duration,track.progress+(track.playing?Math.max(0,Date.now()-track.at):0));$("elapsed").textContent=format(p);
  $("fill").style.width=(track.duration?p/track.duration*100:0)+"%";marquee("title","titleBox");marquee("artist","artistBox");}},100);
applyConfig();
if(demo){const start=Date.now();show({id:"demo",title:"Midnight Drive",artist:"Catego • Preview",image:"",duration:200000,progress:40000,playing:true,at:start},true);
 if(params.get("live")){connect();}}
else{connect();poll();}
`;

export function nowPlayingPage() {
  return `<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Catego Now Playing</title><style>${NP_CSS}</style></head><body>
<section id="card"><div id="art">♫</div><div id="info"><div class="label"><span id="state">NOW PLAYING</span><span id="badge">Spotify</span></div>
<div id="titleBox"><span id="title"></span></div><div id="artistBox"><span id="artist"></span></div>
<div class="bar"><div id="fill"></div></div><div class="times"><span id="elapsed">0:00</span><span id="duration">0:00</span></div></div></section>
<script>${NP_JS}</script></body></html>`;
}
