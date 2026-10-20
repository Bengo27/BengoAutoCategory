// הדפים של האתר (HTML + JS בלי ספריות חיצוניות)

const BASE_STYLE = `
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

const LOGO_SVG = `<svg width="34" height="34" viewBox="0 0 64 64" aria-hidden="true"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
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
<title>Catego – הקראה לסטרימרים</title><style>${BASE_STYLE}
main{max-width:760px;margin:0 auto;padding:56px 18px}
h1{font-size:40px;line-height:1.15;margin:34px 0 14px;letter-spacing:-1px}h1 em{font-style:normal;color:var(--accent)}
.lead{font-size:18px;color:var(--muted);max-width:600px}
.cta{margin:30px 0 40px}.cta .primary{font-size:17px;padding:13px 26px}
.steps{display:grid;grid-template-columns:repeat(3,1fr);gap:14px}.step{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:18px}
.step b{display:block;color:var(--accent);font-size:13px;margin-bottom:6px}.err{color:var(--danger);font-weight:600}
footer{margin-top:44px;font-size:13px;color:var(--muted)}
@media(max-width:640px){h1{font-size:30px}.steps{grid-template-columns:1fr}}
</style></head><body><main>
<div class="logo">${LOGO_SVG}<b>catego</b><span>tts</span></div>
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

// ───────────────────────── לוח הבקרה ─────────────────────────

export function dashboardPage() {
  return `<!doctype html><html lang="he" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Catego – לוח הבקרה</title><style>${BASE_STYLE}
header{display:flex;justify-content:space-between;align-items:center;padding:14px 18px;border-bottom:1px solid var(--line);background:var(--side);position:sticky;top:0;z-index:5}
header .who{display:flex;align-items:center;gap:10px}
main{max-width:900px;margin:0 auto;padding:22px 16px 60px}
.card{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:18px 20px;margin-bottom:14px}
.card h2{font-size:12px;letter-spacing:.06em;color:var(--muted);margin:0 0 12px;font-weight:700}
.row{display:flex;flex-wrap:wrap;gap:10px 14px;align-items:center;justify-content:space-between;padding:9px 0}
.row .lbl{flex:1;min-width:200px}.row .lbl small{display:block;color:var(--muted)}
.actions{display:flex;flex-wrap:wrap;gap:8px;margin-top:14px}
.status-title{font-size:19px;font-weight:700}.dot{display:inline-block;width:10px;height:10px;border-radius:50%;background:var(--muted);margin-inline-end:8px}
.dot.on{background:var(--accent)}.dot.warn{background:var(--warn)}
.switch{position:relative;width:52px;height:28px;flex-shrink:0}.switch input{opacity:0;width:0;height:0}
.switch span{position:absolute;inset:0;background:var(--line);border-radius:28px;transition:.2s;cursor:pointer}
.switch span::before{content:"";position:absolute;width:22px;height:22px;top:3px;right:3px;background:#fff;border-radius:50%;transition:.2s}
.switch input:checked+span{background:var(--accent)}.switch input:checked+span::before{transform:translateX(-24px)}
.chips{display:flex;flex-wrap:wrap;gap:8px}.chip{background:var(--bg);border:1.5px solid var(--line);border-radius:9px;padding:9px 14px;cursor:pointer;text-align:right;color:var(--text)}
.chip small{display:block;color:var(--muted);font-weight:400}.chip.sel{border-color:var(--accent);background:var(--soft)}
.url{display:flex;gap:8px}.url input{direction:ltr;font-family:Consolas,monospace;font-size:13px}
.q{border-top:1px solid var(--line);padding:10px 0;display:flex;gap:12px;align-items:center}.q:first-child{border-top:0}
.q .body{flex:1;min-width:0}.q .top{display:flex;flex-wrap:wrap;gap:4px 10px;align-items:baseline}.q .who{font-weight:700;color:var(--accent)}.q .meta{color:var(--muted);font-size:13px}
.q .text{word-wrap:break-word;margin-top:2px}.q.old{opacity:.55}.q button{padding:5px 12px}
.grid2{display:grid;grid-template-columns:1fr 1fr;gap:12px}.field label{display:block;font-weight:600;margin-bottom:5px}
.field small{color:var(--muted)}.num{width:110px!important;text-align:center;direction:ltr}
#toast{position:fixed;bottom:18px;left:50%;transform:translateX(-50%);background:var(--hover);border:1px solid var(--accent);padding:10px 18px;
border-radius:10px;opacity:0;transition:opacity .3s;pointer-events:none;max-width:90vw}#toast.show{opacity:1}#toast.bad{border-color:var(--danger)}
.notice{background:#2A2410;border:1px solid var(--warn);color:var(--warn);border-radius:10px;padding:10px 14px;margin-top:12px}
.hidden{display:none!important}
@media(max-width:640px){.grid2{grid-template-columns:1fr}.url{flex-wrap:wrap}header .name{display:none}}
</style></head><body>
<header><div class="logo">${LOGO_SVG}<b>catego</b><span>tts</span></div>
<div class="who"><span class="name muted" id="who"></span><form method="post" action="/auth/logout"><button class="ghost" type="submit">התנתק</button></form></div></header>
<main>

<section class="card">
  <div class="row" style="padding-top:0">
    <div class="lbl"><div class="status-title"><span class="dot" id="dot"></span><span id="statusTitle">טוען…</span></div>
    <small id="statusText"></small></div>
    <label class="switch" title="הפעלה / כיבוי"><input type="checkbox" id="enabled"><span></span></label>
  </div>
  <div class="actions">
    <a class="btn primary hidden" id="relogin" href="/auth/login">התחבר מחדש לקיק</a>
    <button id="rewardBtn">צור פרס בקיק</button>
    <button id="pauseBtn">⏸ השהה תור</button>
    <button class="danger" id="skipBtn">⏭ דלג על ההקראה</button>
  </div>
  <div class="notice hidden" id="error"></div>
</section>

<section class="card">
  <h2>הווידג'ט ל-OBS</h2>
  <div class="row" style="padding-top:0"><div class="lbl"><span class="dot" id="obsDot"></span><span id="obsText"></span></div></div>
  <div class="url"><input type="text" id="obsUrl" readonly><button id="copyBtn">העתק</button><button class="ghost" id="rotateBtn">קישור חדש</button></div>
  <p class="muted" style="margin-bottom:0">ב-OBS: מקורות ← <b>+</b> ← <b>Browser</b> ← מדביקים את הקישור, רוחב <b>1920</b> וגובה <b>400</b>, ומסמנים
  <b>Control audio via OBS</b> (במצב שרת). <b>הקישור אישי – אל תשתף אותו.</b></p>
</section>

<section class="card">
  <div class="row" style="padding-top:0"><h2 style="margin:0">תור ההקראות</h2><span class="muted" id="queueSummary"></span></div>
  <div id="queue"></div>
</section>

<section class="card">
  <h2>איך יוצא הקול</h2>
  <div class="chips" id="modes">
    <button class="chip" data-mode="edge">מהלשונית ב-Edge<small>חינם, בלי הגבלה · מומלץ</small></button>
    <button class="chip" data-mode="server">מהשרת (Azure)<small>בלי לשונית פתוחה · מכסה חודשית</small></button>
  </div>
  <div id="edgePanel" style="margin-top:12px">
    <div class="row" style="padding-top:0"><div class="lbl"><span class="dot" id="spkDot"></span><span id="spkText"></span>
      <small id="spkHint"></small></div>
      <button class="primary" id="speakerBtn">🔊 הפעל הקראה מהלשונית הזו</button></div>
    <p class="muted" style="margin:0">משאירים את הלשונית הזו פתוחה ב-Edge <b>במחשב של השידור</b> (אפשר למזער, לא לסגור).
    OBS קולט את הקול דרך Desktop Audio – או, כדי לקבל פס עוצמה נפרד: מקור <b>Application Audio Capture</b> ← <b>msedge.exe</b>.
    הווידג'ט ב-OBS ממשיך להציג את ההודעה על המסך.</p>
  </div>
  <p class="muted hidden" id="serverPanel" style="margin:12px 0 0">הווידג'ט ב-OBS מנגן את הקול בעצמו. ב-Browser Source סמן <b>Control audio via OBS</b> כדי לקבל פס עוצמה משלו.</p>
  <p class="notice hidden" id="serverOff">מצב שרת עוד לא זמין – בעל האתר צריך להגדיר מפתח Azure.</p>
</section>

<section class="card">
  <h2>קול</h2>
  <div class="chips" id="voices">
    <button class="chip" data-voice="he-IL-HilaNeural">Hila<small>אישה</small></button>
    <button class="chip" data-voice="he-IL-AvriNeural">Avri<small>גבר</small></button>
  </div>
  <div class="grid2" style="margin-top:12px">
    <div class="field"><label>מהירות: <span id="rateV"></span></label><input type="range" id="rate" min="-50" max="50" step="5"></div>
    <div class="field"><label>עוצמה: <span id="volumeV"></span></label><input type="range" id="volume" min="0" max="100" step="5"></div>
  </div>
  <div class="field" style="margin-top:10px"><label>טקסט לבדיקה</label><input type="text" id="testText" value="בדיקה, אחת שתיים. ככה ישמעו ההודעות בשידור!"></div>
  <div class="actions"><button class="primary" id="hearBtn">▶ השמע כאן</button><button id="obsTestBtn">שלח בדיקה ל-OBS</button></div>
</section>

<section class="card">
  <h2>הפרס בקיק</h2>
  <div class="grid2">
    <div class="field"><label>שם הפרס</label><input type="text" id="rewardTitle" maxlength="50"></div>
    <div class="field"><label>מחיר (נקודות)</label><input type="number" class="num" id="rewardCost" min="1"></div>
  </div>
  <div class="field" style="margin-top:10px"><label>תיאור לצופים</label><input type="text" id="rewardDescription" maxlength="200"></div>
</section>

<section class="card">
  <h2>מה מוקרא, ושמירה על השידור</h2>
  <div class="field"><label>נוסח ההקראה</label><input type="text" id="template"><small>{user} = שם הצופה, {text} = ההודעה</small></div>
  <div class="row"><div class="lbl">אישור ידני לפני הקראה<small>כל הודעה מחכה עד שתלחץ "הקרא" או "דחה".</small></div>
    <label class="switch"><input type="checkbox" id="manualApproval"><span></span></label></div>
  <div class="row"><div class="lbl">חסימת קישורים<small>הודעה עם קישור נדחית והנקודות חוזרות.</small></div>
    <label class="switch"><input type="checkbox" id="blockLinks"><span></span></label></div>
  <div class="grid2">
    <div class="field"><label>אורך מקסימלי (תווים)</label><input type="number" class="num" id="maxChars" min="10" max="400"></div>
    <div class="field"><label>הפסקה בין הקראות (שניות)</label><input type="number" class="num" id="gapSeconds" min="0" max="60"></div>
    <div class="field"><label>בקשה ישנה נדחית אחרי (דקות)</label><input type="number" class="num" id="maxAgeMinutes" min="1" max="1440"></div>
  </div>
  <div class="field" style="margin-top:12px"><label>מילים חסומות (מילה או ביטוי בכל שורה)</label>
    <textarea id="banned"></textarea><small>נתפס גם עם ו/ה/ש/ב/כ/ל/מ בהתחלה. הודעה עם מילה חסומה נדחית והנקודות חוזרות.</small></div>
</section>

<div class="actions"><button class="primary" id="saveBtn">שמור הגדרות</button><span class="muted" id="saved"></span></div>
</main>
<div id="toast"></div>
<script>
const $=id=>document.getElementById(id);let state=null,formLoaded=false,voice="he-IL-HilaNeural",mode="edge",speakerWs=null,speakerOn=false;
const STATUS={playing:"🔊 מוקרא עכשיו",waiting:"בתור",approval:"ממתין לאישור",done:"הוקרא",skipped:"דולג",rejected:"נדחה – הנקודות הוחזרו",failed:"נכשל"};
function toast(t,bad){const e=$("toast");e.textContent=t;e.className="show"+(bad?" bad":"");clearTimeout(e._t);e._t=setTimeout(()=>e.className="",3500);}
async function post(op,body={}){const r=await fetch("/api/"+op,{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
 if(r.status===401){location.href="/";return;}const ct=r.headers.get("Content-Type")||"";
 if(ct.includes("audio"))return r.blob();const j=await r.json().catch(()=>({}));if(!r.ok)throw Error(j.error||"הפעולה נכשלה");if(j.state)render(j.state);return j;}
function act(id,fn){$(id).onclick=async()=>{const b=$(id);b.disabled=true;try{await fn();}catch(e){toast(e.message,true);}finally{b.disabled=false;}};}
function setVoice(v){voice=v;document.querySelectorAll(".chip").forEach(c=>c.classList.toggle("sel",c.dataset.voice===v));}
document.querySelectorAll("#voices .chip").forEach(c=>c.onclick=()=>setVoice(c.dataset.voice));
function setMode(m){mode=m;document.querySelectorAll("#modes .chip").forEach(c=>c.classList.toggle("sel",c.dataset.mode===m));
 $("edgePanel").classList.toggle("hidden",m!=="edge");$("serverPanel").classList.toggle("hidden",m!=="server");}
document.querySelectorAll("#modes .chip").forEach(c=>c.onclick=async()=>{const m=c.dataset.mode;if(m==="server"&&state&&!state.serverTts){toast("מצב שרת עוד לא הוגדר (חסר מפתח Azure)",true);return;}
 setMode(m);try{await post("settings",{settings:{playback:m}});toast(m==="edge"?"הקול יוצא מהלשונית ב-Edge":"הקול יוצא מהשרת דרך הווידג'ט");}catch(e){toast(e.message,true);}});
// ── הקראה מהלשונית (Web Speech של הדפדפן – ב-Edge יש את Hila ו-Avri) ──
const isEdge=navigator.userAgent.includes("Edg/");
function heVoices(){return(speechSynthesis.getVoices()||[]).filter(v=>/^(he|iw)/i.test(v.lang));}
const VOICE_RE={"he-IL-AvriNeural":/avri|אברי/i,"he-IL-HilaNeural":/hila|הילה/i};
function pickVoice(id){const want=VOICE_RE[id]||VOICE_RE["he-IL-HilaNeural"];const he=heVoices();
 return he.find(v=>want.test(v.name)||want.test(v.voiceURI))||he.find(v=>/natural|online/i.test(v.name))||he[0]||null;}
const shortName=v=>v?v.name.replace(/^Microsoft\s+/i,"").replace(/\s*-\s*Hebrew.*$/i,""):"";
let utter=null;
function speak(text,voiceId,rate,volume,onEnd){if(!("speechSynthesis" in window)){onEnd("no-speech");return;}
 const v=pickVoice(voiceId);if(!v){onEnd("no-voice");return;}
 // ביטול ממש לפני speak גורם לפעמים לדפדפן "לבלוע" את ההקראה – מבטלים רק אם משהו מתנגן, ומחכים רגע
 const busy=speechSynthesis.speaking||speechSynthesis.pending;if(busy)speechSynthesis.cancel();
 const u=new SpeechSynthesisUtterance(text);u.voice=v;u.lang=v.lang;u.rate=Math.max(.5,Math.min(2,1+rate/100));u.volume=volume;
 let finished=false;const end=e=>{if(finished)return;finished=true;onEnd(e);};u.onend=()=>end(null);u.onerror=ev=>end(ev.error==="interrupted"||ev.error==="canceled"?null:"speech-error");
 utter=u;setTimeout(()=>speechSynthesis.speak(u),busy?150:0);return v;}
function speakerStatus(){const he=heVoices();const hila=he.find(v=>VOICE_RE["he-IL-HilaNeural"].test(v.name)),avri=he.find(v=>VOICE_RE["he-IL-AvriNeural"].test(v.name));
 $("spkDot").className="dot "+(speakerOn?"on":"warn");
 $("spkText").textContent=speakerOn?"הלשונית הזו מקריאה את ההודעות ✓":(state&&state.speakers?"לשונית אחרת מקריאה ✓":"אף לשונית לא מקריאה כרגע");
 $("spkHint").textContent=!("speechSynthesis" in window)?"הדפדפן הזה לא יכול להקריא.":!isEdge?"מומלץ לפתוח את הדף ב-Microsoft Edge – רק בו יש את Hila ו-Avri.":!he.length?"לא נמצאו קולות עבריים בדפדפן הזה.":"קולות שנמצאו: "+he.map(shortName).join(", ")+(hila&&avri?" ✓":" – "+(!hila?"Hila":"")+(!hila&&!avri?" ו-":"")+(!avri?"Avri":"")+" חסר, יוקרא בקול אחר");
 $("speakerBtn").textContent=speakerOn?"⏹ הפסק להקריא מכאן":"🔊 הפעל הקראה מהלשונית הזו";}
if("speechSynthesis" in window)speechSynthesis.onvoiceschanged=speakerStatus;
let spkRetry=1000;
function connectSpeaker(){speakerWs=new WebSocket((location.protocol==="https:"?"wss://":"ws://")+location.host+"/api/ws?speaker=1");
 speakerWs.onmessage=e=>{spkRetry=1000;let m;try{m=JSON.parse(e.data);}catch{return;}
  if(m.type==="speak"){const id=m.id;speak(m.text,m.voice,m.rate,m.volume,err=>{try{speakerWs.send(JSON.stringify({type:"done",id,error:err||undefined}));}catch{}});}
  else if(m.type==="stop"){speechSynthesis.cancel();}};
 speakerWs.onclose=e=>{if(!speakerOn)return;if(e.code===4001){location.href="/";return;}
  if(e.code===4002){speakerOn=false;speakerStatus();toast("לשונית אחרת התחילה להקריא – הלשונית הזו הפסיקה");return;}setTimeout(connectSpeaker,spkRetry);spkRetry=Math.min(spkRetry*2,15000);};}
$("speakerBtn").onclick=()=>{if(speakerOn){speakerOn=false;try{speakerWs.close();}catch{}speechSynthesis.cancel();speakerStatus();return;}
 if(!("speechSynthesis" in window)||!heVoices().length){toast("בדפדפן הזה אין קולות עבריים – פתח את האתר ב-Microsoft Edge",true);return;}
 // הקלקה הזו גם "מאשרת" לדפדפן להשמיע קול מהדף
 const warm=new SpeechSynthesisUtterance(" ");warm.volume=0;speechSynthesis.speak(warm);
 speakerOn=true;connectSpeaker();speakerStatus();toast("הלשונית הזו תקריא את ההודעות – השאר אותה פתוחה");};
window.addEventListener("beforeunload",e=>{if(speakerOn){e.preventDefault();e.returnValue="";}});
function showRange(){const r=+$("rate").value;$("rateV").textContent=r===0?"רגילה":(r>0?"+":"")+r+"%";$("volumeV").textContent=$("volume").value+"%";}
$("rate").oninput=$("volume").oninput=showRange;
function loadForm(s){for(const k of["rewardTitle","rewardCost","rewardDescription","template","maxChars","gapSeconds","maxAgeMinutes","rate","volume"])$(k).value=s[k];
 $("manualApproval").checked=s.manualApproval;$("blockLinks").checked=s.blockLinks;$("banned").value=(s.banned||[]).join("\\n");setVoice(s.voice);showRange();}
function collect(){return{rewardTitle:$("rewardTitle").value,rewardCost:+$("rewardCost").value,rewardDescription:$("rewardDescription").value,template:$("template").value,
 maxChars:+$("maxChars").value,gapSeconds:+$("gapSeconds").value,maxAgeMinutes:+$("maxAgeMinutes").value,rate:+$("rate").value,volume:+$("volume").value,
 manualApproval:$("manualApproval").checked,blockLinks:$("blockLinks").checked,voice,playback:mode,banned:$("banned").value.split(/\\n|,/).map(x=>x.trim()).filter(Boolean)};}
function render(s){state=s;const st=s.settings;if(!formLoaded){loadForm(st);formLoaded=true;}
 $("who").textContent=s.user?.name||"";$("enabled").checked=st.enabled;$("obsUrl").value=location.origin+s.overlayPath;
 $("rewardBtn").textContent=s.rewardId?"עדכן את הפרס בקיק":"צור פרס בקיק";$("pauseBtn").textContent=s.paused?"▶ המשך תור":"⏸ השהה תור";
 $("skipBtn").disabled=!s.playingId;$("relogin").classList.toggle("hidden",s.connected&&s.rewardsScope);
 let title,text,dot="";
 if(!s.connected){title="החיבור לקיק פג";text="התחבר מחדש כדי שההקראה תמשיך לעבוד.";dot="warn";}
 else if(!s.rewardsScope){title="צריך להתחבר מחדש לקיק";text="ההתחברות נעשתה בלי הרשאת פרסים.";dot="warn";}
 else if(!s.rewardId){title="עוד אין פרס הקראה בערוץ";text="בחר שם ומחיר למטה ולחץ \\"צור פרס בקיק\\" (או הדלק את המתג).";}
 else if(st.enabled){title=s.paused?"ההקראה פעילה · התור מושהה":"ההקראה פעילה 🔊";text="צופים יכולים לקנות \\""+st.rewardTitle+"\\" ב-"+st.rewardCost.toLocaleString()+" נקודות.";dot="on";}
 else{title="ההקראה כבויה";text="הפרס בקיק מושהה – צופים לא יכולים לקנות אותו עד שתדליק.";}
 $("statusTitle").textContent=title;$("statusText").textContent=text;$("dot").className="dot "+dot;
 $("error").textContent=s.error||"";$("error").classList.toggle("hidden",!s.error);
 speakerStatus();$("serverOff").classList.toggle("hidden",s.serverTts||st.playback!=="server");
 $("obsDot").className="dot "+(s.overlays?"on":"warn");$("obsText").textContent=s.overlays?"הווידג'ט פתוח ב-OBS ✓":"הווידג'ט לא פתוח – הוסף אותו ל-OBS (או פתח את OBS)";
 const q=$("queue");q.replaceChildren();const open=s.items.filter(i=>["playing","waiting","approval"].includes(i.status)).length;
 $("queueSummary").textContent=open?open+" ממתינים":"התור ריק";
 if(!s.items.length){const p=document.createElement("p");p.className="muted";p.style.margin="0";p.textContent="עוד אין הודעות. ברגע שצופה יקנה את הפרס – ההודעה תופיע כאן.";q.append(p);}
 for(const i of s.items){const row=document.createElement("div");row.className="q"+(["playing","waiting","approval"].includes(i.status)?"":" old");
  const body=document.createElement("div");body.className="body";const top=document.createElement("div");top.className="top";
  const who=document.createElement("span");who.className="who";who.textContent=i.user;const meta=document.createElement("span");meta.className="meta";
  meta.textContent=new Date(i.at).toLocaleTimeString("he-IL",{hour:"2-digit",minute:"2-digit"})+" · "+(STATUS[i.status]||i.status)+(i.note?" · "+i.note:"");
  top.append(who,meta);const tx=document.createElement("div");tx.className="text";tx.textContent=i.text;body.append(top,tx);row.append(body);
  if(i.status==="approval"){const b=document.createElement("button");b.className="primary";b.textContent="✓ הקרא";b.onclick=()=>post("approve",{id:i.id}).catch(e=>toast(e.message,true));row.append(b);}
  if((i.status==="approval"||i.status==="waiting")&&!i.test){const b=document.createElement("button");b.textContent="✕ דחה";b.title="דוחה ומחזיר לצופה את הנקודות";b.onclick=()=>post("reject",{id:i.id}).catch(e=>toast(e.message,true));row.append(b);}
  q.append(row);}}
$("enabled").onchange=async e=>{try{await post("enable",{enabled:e.target.checked});}catch(err){toast(err.message,true);e.target.checked=!e.target.checked;}};
act("rewardBtn",async()=>{await post("settings",{settings:collect()});await post("reward");toast("✓ הפרס מוכן בקיק");});
act("pauseBtn",()=>post("pause",{paused:!state?.paused}));act("skipBtn",()=>post("skip"));
act("saveBtn",async()=>{await post("settings",{settings:collect()});$("saved").textContent="נשמר ב-"+new Date().toLocaleTimeString("he-IL");formLoaded=false;render(state);});
act("copyBtn",async()=>{await navigator.clipboard.writeText($("obsUrl").value);toast("הקישור הועתק ✓ – הדבק אותו ב-OBS");});
act("rotateBtn",async()=>{if(!confirm("ליצור קישור חדש? הקישור הישן יפסיק לעבוד, וצריך להדביק את החדש ב-OBS."))return;await post("rotate");toast("נוצר קישור חדש");});
act("hearBtn",async()=>{if(mode==="edge"){await new Promise(res=>{const used=speak($("testText").value||"בדיקת קול",voice,+$("rate").value,+$("volume").value/100,err=>{if(err)toast(err==="no-voice"?"לא נמצא קול עברי בדפדפן – פתח ב-Edge":"ההקראה נכשלה ("+err+")",true);res();});if(used)toast("מקריא בקול: "+shortName(used));});return;}
 const blob=await post("voice-test",{text:$("testText").value,voice,rate:+$("rate").value});const a=new Audio(URL.createObjectURL(blob));a.volume=+$("volume").value/100;await a.play();});
act("obsTestBtn",async()=>{await post("settings",{settings:collect()});await post("test",{text:$("testText").value});toast(mode==="edge"?(state?.speakers?"נשלח – תשמע מהלשונית המקריאה ותראה ב-OBS":"נשמר – יוקרא כשתפעיל הקראה מהלשונית"):(state?.overlays?"נשלח ל-OBS":"נשמר – יושמע כשהווידג'ט ייפתח ב-OBS"));});
let retry=1000;function connect(){const ws=new WebSocket((location.protocol==="https:"?"wss://":"ws://")+location.host+"/api/ws");
 ws.onmessage=e=>{retry=1000;try{const m=JSON.parse(e.data);if(m.type==="state")render(m.state);}catch{}};
 ws.onclose=e=>{if(e.code===4001){location.href="/";return;}setTimeout(connect,retry);retry=Math.min(retry*2,15000);};}
fetch("/api/state").then(r=>r.status===401?(location.href="/"):r.json()).then(s=>s&&s.settings&&render(s)).catch(()=>{});connect();
</script></body></html>`;
}

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
