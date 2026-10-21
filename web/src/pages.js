// הדפים של האתר (HTML + JS בלי ספריות חיצוניות)

export const BASE_STYLE = `
:root{--bg:#0E0F13;--side:#14161B;--card:#1A1D24;--hover:#22262F;--line:#2A2E38;--text:#E8EAED;--muted:#9AA0A6;
--accent:#2DD4BF;--accent2:#5EEAD4;--warn:#F5B841;--danger:#F2545B;--soft:#163A36}
*{box-sizing:border-box}html,body{margin:0;background:var(--bg);color:var(--text);font:15px/1.55 "Segoe UI",Arial,sans-serif}
a{color:var(--accent)}button,.btn{font:inherit;font-weight:600;border:1px solid var(--line);background:var(--hover);color:var(--text);
border-radius:9px;padding:9px 16px;cursor:pointer;text-decoration:none;display:inline-block;white-space:nowrap}
button:hover,.btn:hover{border-color:var(--accent)}button:disabled{opacity:.5;cursor:default}
.primary{background:var(--accent);border-color:var(--accent);color:#0B1514}.primary:hover{background:var(--accent2)}
.danger{background:transparent;border-color:var(--danger);color:var(--danger)}
.ghost{background:transparent;border-color:transparent;color:var(--muted)}
input[type=text],input[type=number],textarea{width:100%;font:inherit;color:var(--text);background:var(--bg);border:1px solid var(--line);
border-radius:8px;padding:9px 11px}textarea{resize:vertical;min-height:90px}input:focus,textarea:focus{outline:none;border-color:var(--accent)}
input[type=range]{width:100%;accent-color:var(--accent)}
.logo{display:flex;align-items:center;gap:10px;direction:ltr}.logo b{font-size:22px;font-weight:900;letter-spacing:-.5px}
.logo span{color:var(--accent);font-weight:600}
.muted{color:var(--muted)}
`;

export const LOGO_SVG = `<svg width="34" height="34" viewBox="0 0 64 64" aria-hidden="true"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
<stop offset="0" stop-color="#5EEAD4"/><stop offset="1" stop-color="#0D9488"/></linearGradient></defs>
<rect width="64" height="64" rx="16" fill="#111018"/><path d="M14 22l6-10 6 8h12l6-8 6 10v18c0 7-6 12-13 12H27c-7 0-13-5-13-12z" fill="url(#g)"/>
<circle cx="25" cy="32" r="3" fill="#111018"/><circle cx="39" cy="32" r="3" fill="#111018"/><path d="M28 40q4 3 8 0" stroke="#111018" stroke-width="2.5" fill="none" stroke-linecap="round"/></svg>`;

// ───────────────────────── דף הבית ─────────────────────────

const ERRORS = {
  denied: "ההתחברות בוטלה בדף של קיק.",
  state: "ההתחברות פגה. נסה שוב.",
  kick: "קיק לא אישר את ההתחברות. נסה שוב בעוד רגע.",
  config: "האתר עוד לא הוגדר עד הסוף (חסרים פרטי האפליקציה של קיק).",
};

export function landingPage({ error } = {}) {
  const err = ERRORS[error] ? `<p class="err">${ERRORS[error]}</p>` : "";
  return `<!doctype html><html lang="he" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Catego Studio</title><style>${BASE_STYLE}
main{max-width:760px;margin:0 auto;padding:56px 18px}
h1{font-size:40px;line-height:1.15;margin:34px 0 14px;letter-spacing:-1px}h1 em{font-style:normal;color:var(--accent)}
.lead{font-size:18px;color:var(--muted);max-width:600px}
.cta{margin:30px 0 40px}.cta .primary{font-size:17px;padding:13px 26px}
.steps{display:grid;grid-template-columns:repeat(3,1fr);gap:14px}.step{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:18px}
.step b{display:block;color:var(--accent);font-size:13px;margin-bottom:6px}.err{color:var(--danger);font-weight:600}
footer{margin-top:44px;font-size:13px;color:var(--muted)}
@media(max-width:640px){h1{font-size:30px}.steps{grid-template-columns:1fr}}
</style></head><body><main>
<div class="logo">${LOGO_SVG}<b>catego</b><span>studio</span></div>
<h1>צופים קונים בנקודות – <em>וההודעה שלהם מוקראת בשידור</em></h1>
<p class="lead">פרס הקראה בערוץ הקיק שלך, בקולות עבריים טבעיים (Hila ו-Avri). בלי להתקין כלום – רק קישור אחד ב-OBS.</p>
${err}
<div class="cta"><a class="btn primary" href="/auth/login">התחבר עם קיק</a></div>
<div class="steps">
<div class="step"><b>1 · מתחברים</b>בדף הרשמי של קיק. הסיסמה לא עוברת דרכנו.</div>
<div class="step"><b>2 · יוצרים פרס</b>בוחרים קול, מחיר ומילים חסומות – והפרס מופיע לצופים.</div>
<div class="step"><b>3 · מוסיפים ל-OBS</b>מעתיקים את הקישור האישי ל-Browser Source. זהו.</div>
</div>
<footer>הודעה שנחסמת או נדחית מחזירה לצופה את הנקודות. ההרשאות: פרטי משתמש וערוץ, וניהול פרסי נקודות – בלי צ'אט ובלי מפתח שידור.</footer>
</main></body></html>`;
}

// לוח הבקרה נמצא ב-dashboard.js

// ───────────────────────── הווידג'ט ל-OBS ─────────────────────────

export function overlayPage() {
  return `<!doctype html><html lang="he" dir="rtl"><head><meta charset="utf-8"><title>Catego TTS</title><style>
html,body{margin:0;background:transparent;overflow:hidden}
body{font-family:"Segoe UI",Arial,sans-serif;display:flex;align-items:flex-end;justify-content:center;height:100vh;padding:24px;box-sizing:border-box}
#card{max-width:900px;background:rgba(17,16,24,.92);border-right:6px solid #2DD4BF;border-radius:14px;padding:18px 26px;color:#fff;
box-shadow:0 10px 30px rgba(0,0,0,.45);opacity:0;transform:translateY(30px);transition:opacity .35s ease,transform .35s ease}
#card.show{opacity:1;transform:translateY(0)}#user{color:#2DD4BF;font-weight:800;font-size:26px;margin-bottom:4px}#user::before{content:"🔊 "}
#text{font-size:30px;line-height:1.35;font-weight:600;word-wrap:break-word}
</style></head><body><div id="card"><div id="user"></div><div id="text"></div></div><audio id="audio"></audio><script>
const card=document.getElementById("card"),userEl=document.getElementById("user"),textEl=document.getElementById("text"),audio=document.getElementById("audio");
const base=location.pathname.replace(/\\/$/,"");let ws=null,current=null,retry=1000;
function show(u,t){userEl.textContent=u;textEl.textContent=t;card.classList.add("show");}
function hide(){card.classList.remove("show");audio.pause();audio.removeAttribute("src");current=null;}
function done(id){if(current!==id)return;hide();try{ws&&ws.send(JSON.stringify({type:"done",id}));}catch{}}
if(new URLSearchParams(location.search).has("demo")){show("Bengo23","ככה תיראה הודעת הקראה בשידור. אפשר להזיז ולשנות גודל ב-OBS.");}
else{(function connect(){ws=new WebSocket((location.protocol==="https:"?"wss://":"ws://")+location.host+base+"/ws");
 ws.onopen=()=>{retry=1000;};
 ws.onmessage=e=>{let m;try{m=JSON.parse(e.data);}catch{return;}
  if(m.type==="play"&&m.silent){current=m.id;show(m.user,m.text);}
  else if(m.type==="play"){current=m.id;show(m.user,m.text);audio.src=base+"/audio?id="+encodeURIComponent(m.id);audio.volume=Math.max(0,Math.min(1,m.volume));
   const id=m.id;audio.onended=()=>done(id);audio.onerror=()=>done(id);audio.play().catch(()=>done(id));}
  else if((m.type==="stop"||m.type==="end")&&m.id===current){hide();}};
 ws.onclose=e=>{if(e.code===4003)return;setTimeout(connect,retry);retry=Math.min(retry*2,15000);};})();}
</script></body></html>`;
}
