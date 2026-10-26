// לוח הבקרה החדש: תפריט צד, סקירה, הקראה (TTS), מקורות OBS, הגדרות – ומקום לכלים הבאים.
import { BASE_STYLE, LOGO_SVG } from "./pages.js";

const STYLE = `
body{min-height:100vh}
.layout{display:grid;grid-template-columns:250px 1fr;min-height:100vh}
aside{background:var(--side);border-inline-start:1px solid var(--line);padding:18px 14px;position:sticky;top:0;height:100vh;overflow-y:auto;display:flex;flex-direction:column;gap:14px}
.chan{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:12px}
.chan b{display:block;font-size:15px}.chan small{color:var(--muted)}
.live{display:inline-flex;align-items:center;gap:6px;font-size:12px;font-weight:700;padding:2px 8px;border-radius:99px;background:var(--hover);color:var(--muted)}
.live.on{background:#53fc1822;color:#53fc18}.live::before{content:"";width:7px;height:7px;border-radius:50%;background:currentColor}
nav{display:flex;flex-direction:column;gap:2px}
nav .grp{font-size:11px;font-weight:700;letter-spacing:.06em;color:var(--muted);margin:12px 10px 4px}
nav a{display:flex;align-items:center;gap:10px;color:var(--text);text-decoration:none;padding:9px 10px;border-radius:9px;font-weight:600}
nav a:hover{background:var(--hover)}nav a.sel{background:var(--soft);color:var(--accent)}
nav a.soon{color:var(--muted);cursor:default}nav a.soon:hover{background:none}
nav a .tag{margin-inline-start:auto;font-size:10px;font-weight:700;border:1px solid var(--line);border-radius:99px;padding:0 7px;color:var(--muted)}
aside .bottom{margin-top:auto;display:flex;justify-content:space-between;align-items:center;color:var(--muted);font-size:13px}
main{padding:24px 26px 70px;max-width:1000px;width:100%}
.page{display:none}.page.sel{display:block}
h1{font-size:26px;margin:0 0 4px}.sub{color:var(--muted);margin:0 0 18px}
.card{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:18px 20px;margin-bottom:14px}
.card h2{font-size:12px;letter-spacing:.06em;color:var(--muted);margin:0 0 12px;font-weight:700}
.tiles{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-bottom:14px}
.tile{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:16px}
.tile b{display:block;font-size:30px;font-weight:800;line-height:1.1}.tile span{color:var(--muted);font-size:13px}
.svc{display:flex;align-items:center;gap:12px;padding:10px 0;border-top:1px solid var(--line)}.svc:first-of-type{border-top:0}
.svc .n{flex:1;font-weight:600}.svc .v{color:var(--muted);font-size:14px}
.dot{display:inline-block;width:10px;height:10px;border-radius:50%;background:var(--muted);flex-shrink:0}.dot.on{background:var(--accent)}.dot.warn{background:var(--warn)}.dot.bad{background:var(--danger)}
.row{display:flex;flex-wrap:wrap;gap:10px 14px;align-items:center;justify-content:space-between;padding:9px 0}
.row .lbl{flex:1;min-width:200px}.row .lbl small{display:block;color:var(--muted)}
.actions{display:flex;flex-wrap:wrap;gap:8px}
.switch{position:relative;width:52px;height:28px;flex-shrink:0}.switch input{opacity:0;width:0;height:0}
.switch span{position:absolute;inset:0;background:var(--line);border-radius:28px;transition:.2s;cursor:pointer}
.switch span::before{content:"";position:absolute;width:22px;height:22px;top:3px;right:3px;background:#fff;border-radius:50%;transition:.2s}
.switch input:checked+span{background:var(--accent)}.switch input:checked+span::before{transform:translateX(-24px)}
.big-status{display:flex;align-items:center;gap:14px}.big-status .t{font-size:19px;font-weight:700}
.chips{display:flex;flex-wrap:wrap;gap:8px}.chip{background:var(--bg);border:1.5px solid var(--line);border-radius:9px;padding:9px 14px;cursor:pointer;text-align:right;color:var(--text);white-space:normal}
.chip small{display:block;color:var(--muted);font-weight:400}.chip.sel{border-color:var(--accent);background:var(--soft)}
.tabs{display:flex;gap:6px;flex-wrap:wrap;margin-bottom:14px;border-bottom:1px solid var(--line);padding-bottom:10px}
.tabs button{background:transparent;border-color:transparent;color:var(--muted)}.tabs button.sel{background:var(--soft);color:var(--accent);border-color:var(--soft)}
.tab{display:none}.tab.sel{display:block}
.grid2{display:grid;grid-template-columns:1fr 1fr;gap:12px}.field label{display:block;font-weight:600;margin-bottom:5px}.field small{color:var(--muted)}
.num{width:110px!important;text-align:center;direction:ltr}
.url{display:flex;gap:8px}.url input{direction:ltr;font-family:Consolas,monospace;font-size:13px}
.q{border-top:1px solid var(--line);padding:10px 0;display:flex;gap:12px;align-items:center}.q:first-child{border-top:0}
.q .body{flex:1;min-width:0}.q .top{display:flex;flex-wrap:wrap;gap:4px 10px;align-items:baseline}
.q .who{font-weight:700;color:var(--accent)}.q .meta{color:var(--muted);font-size:13px}.q .text{word-wrap:break-word;margin-top:2px}.q.old{opacity:.55}.q button{padding:5px 12px}
.banner{display:flex;align-items:center;gap:12px;background:#2A2410;border:1px solid var(--warn);border-radius:12px;padding:12px 16px;margin-bottom:14px}
.banner .t{flex:1;color:var(--warn);font-weight:600}
.notice{background:#2A2410;border:1px solid var(--warn);color:var(--warn);border-radius:10px;padding:10px 14px;margin-top:12px}
.soon-card{display:flex;gap:14px;align-items:flex-start}.soon-card .ic{font-size:26px}
.savebar{position:sticky;bottom:0;background:linear-gradient(transparent,var(--bg) 30%);padding:18px 0 6px;display:flex;gap:12px;align-items:center}
#toast{position:fixed;bottom:18px;left:50%;transform:translateX(-50%);background:var(--hover);border:1px solid var(--accent);padding:10px 18px;border-radius:10px;opacity:0;transition:opacity .3s;pointer-events:none;max-width:90vw;z-index:50}
#toast.show{opacity:1}#toast.bad{border-color:var(--danger)}
.hidden{display:none!important}
@media(max-width:820px){.layout{grid-template-columns:1fr}aside{position:static;height:auto;padding:12px}nav{flex-direction:row;overflow-x:auto}
 nav .grp,aside .bottom,.chan{display:none}nav a{white-space:nowrap}main{padding:16px 14px 60px}.tiles{grid-template-columns:1fr 1fr}.grid2{grid-template-columns:1fr}.url{flex-wrap:wrap}}
`;

const BODY = `
<div class="layout">
<aside>
  <div class="logo">${LOGO_SVG}<b>catego</b><span>studio</span></div>
  <div class="chan"><b id="chanName">…</b><small id="chanLink"></small><div style="margin-top:8px"><span class="live" id="chanLive">לא בלייב</span></div></div>
  <nav>
    <a href="#overview" data-page="overview">🏠 סקירה</a>
    <div class="grp">כלים</div>
    <a href="#tts" data-page="tts">🔊 הקראה (TTS)</a>
    <a href="#np" data-page="np">🎵 Now Playing</a>
    <a class="soon">🔔 התראות <span class="tag">בקרוב</span></a>
    <a class="soon">📊 סקרים וניחושים <span class="tag">בקרוב</span></a>
    <a class="soon">💬 צ'אט על המסך <span class="tag">בקרוב</span></a>
    <a class="soon">🎯 בר יעד <span class="tag">בקרוב</span></a>
    <div class="grp">ניהול</div>
    <a href="#obs" data-page="obs">🖥️ מקורות OBS</a>
    <a href="#settings" data-page="settings">⚙️ הגדרות</a>
  </nav>
  <div class="bottom"><span id="who"></span><form method="post" action="/auth/logout"><button class="ghost" type="submit">התנתק</button></form></div>
</aside>

<main>
  <div class="banner hidden" id="spkBanner"><span class="t" id="spkBannerText">אף לשונית לא מקריאה כרגע – ההקראות מחכות.</span>
    <button class="primary" id="spkBannerBtn">🔊 הפעל הקראה מכאן</button><button class="hidden" id="spkBannerGemini">עבור ל-Gemini</button></div>
  <div class="banner hidden" id="reloginBanner"><span class="t" id="reloginText"></span><a class="btn primary" href="/auth/login">התחבר מחדש</a></div>

  <!-- ═══════ סקירה ═══════ -->
  <section class="page" id="page-overview">
    <h1>סקירה</h1><p class="sub" id="ovSub">כל מה שקורה בערוץ במבט אחד.</p>
    <div class="tiles">
      <div class="tile"><b id="stTotal">0</b><span>הקראות היום</span></div>
      <div class="tile"><b id="stChat">0</b><span>מהצ'אט</span></div>
      <div class="tile"><b id="stReward">0</b><span>מפרסים בנקודות</span></div>
      <div class="tile"><b id="stQueue">0</b><span>בתור עכשיו</span></div>
    </div>
    <div class="card">
      <div class="big-status"><span class="dot" id="dot"></span><div style="flex:1"><div class="t" id="statusTitle">טוען…</div><small class="muted" id="statusText"></small></div>
        <label class="switch" title="הפעלה / כיבוי של ההקראה"><input type="checkbox" id="enabled"><span></span></label></div>
      <div class="notice hidden" id="error"></div>
    </div>
    <div class="card"><h2>פעולות מהירות</h2>
      <div class="actions"><button id="qaTest">🧪 בדיקת הקראה</button><button class="danger" id="qaSkip">⏭ דלג על ההקראה</button>
        <button id="qaPause">⏸ השהה תור</button><button id="qaClear">🗑 נקה תור</button></div>
    </div>
    <div class="card"><h2>שירותים</h2>
      <div class="svc"><span class="dot" id="svKick"></span><span class="n">חשבון קיק</span><span class="v" id="svKickV"></span></div>
      <div class="svc"><span class="dot" id="svHook"></span><span class="n">התראות מקיק (קניית פרסים)</span><span class="v" id="svHookV"></span></div>
      <div class="svc"><span class="dot" id="svChat"></span><span class="n">הודעות צ'אט</span><span class="v" id="svChatV"></span></div>
      <div class="svc"><span class="dot" id="svObs"></span><span class="n">ווידג'ט ב-OBS</span><span class="v" id="svObsV"></span></div>
      <div class="svc"><span class="dot" id="svVoice"></span><span class="n" id="svVoiceN">לשונית מקריאה</span><span class="v" id="svVoiceV"></span></div>
    </div>
  </section>

  <!-- ═══════ הקראה ═══════ -->
  <section class="page" id="page-tts">
    <h1>הקראה (TTS)</h1><p class="sub">צופים שולחים הודעה – והיא מוקראת בשידור. דרך פרס בנקודות, או מהצ'אט עם סימן.</p>
    <div class="tabs" id="ttsTabs">
      <button data-tab="queue" class="sel">תור</button><button data-tab="voice">קול</button>
      <button data-tab="chat">💬 מהצ'אט</button><button data-tab="alerts">🔔 אירועים</button><button data-tab="reward">🎁 פרס בנקודות</button><button data-tab="filter">🛡️ סינון</button>
    </div>

    <div class="tab sel" id="tab-queue"><div class="card">
      <div class="row" style="padding-top:0"><h2 style="margin:0">תור ההקראות</h2><span class="muted" id="queueSummary"></span></div>
      <div id="queue"></div></div></div>

    <div class="tab" id="tab-voice">
      <div class="card"><h2>איך יוצא הקול</h2>
        <div class="chips" id="modes">
          <button class="chip" data-mode="edge">מהלשונית ב-Edge<small>חינם, בלי הגבלה · מומלץ</small></button>
          <button class="chip" data-mode="gemini">מהשרת (Gemini)<small>חינם, בלי לשונית · מגבלה יומית</small></button>
          <button class="chip" data-mode="server">מהשרת (Azure)<small>Hila ו-Avri בלי לשונית · מכסה חודשית</small></button>
        </div>
        <div id="edgePanel" style="margin-top:12px">
          <div class="row" style="padding-top:0"><div class="lbl"><span id="spkText"></span><small id="spkHint"></small></div>
            <button class="primary" id="speakerBtn">🔊 הפעל הקראה מהלשונית הזו</button></div>
          <p class="muted" style="margin:0">משאירים את הלשונית פתוחה ב-Edge <b>במחשב של השידור</b> (אפשר למזער, לא לסגור). OBS קולט את הקול דרך
          Desktop Audio – או, לפס עוצמה נפרד: מקור <b>Application Audio Capture</b> ← <b>msedge.exe</b>.</p>
        </div>
        <p class="muted hidden" id="serverPanel" style="margin:12px 0 0">הווידג'ט ב-OBS מנגן את הקול בעצמו. ב-Browser Source סמן <b>Control audio via OBS</b>.</p>
        <p class="notice hidden" id="serverOff"></p>
      </div>
      <div class="card"><h2>קול</h2>
        <div class="chips" id="voices">
          <button class="chip" data-voice="he-IL-HilaNeural">Hila<small>אישה</small></button>
          <button class="chip" data-voice="he-IL-AvriNeural">Avri<small>גבר</small></button>
        </div>
        <div class="chips hidden" id="gvoices">
          <button class="chip" data-gvoice="Kore">Kore<small>אישה, רגועה</small></button>
          <button class="chip" data-gvoice="Aoede">Aoede<small>אישה, קלילה</small></button>
          <button class="chip" data-gvoice="Zephyr">Zephyr<small>אישה, שמחה</small></button>
          <button class="chip" data-gvoice="Puck">Puck<small>גבר, אנרגטי</small></button>
          <button class="chip" data-gvoice="Charon">Charon<small>גבר, רציני</small></button>
          <button class="chip" data-gvoice="Fenrir">Fenrir<small>גבר, נלהב</small></button>
        </div>
        <div class="grid2" style="margin-top:12px">
          <div class="field"><label>מהירות: <span id="rateV"></span></label><input type="range" id="rate" min="-50" max="50" step="5"></div>
          <div class="field"><label>עוצמה: <span id="volumeV"></span></label><input type="range" id="volume" min="0" max="100" step="5"></div>
        </div>
        <div class="row" style="margin-top:6px"><div class="lbl">להקריא את שם הכותב<small>כבוי = מוקראת רק ההודעה עצמה, בלי "דני אומר:". (בהקראת אירועים השם נשאר – זו התודה.)</small></div>
          <label class="switch"><input type="checkbox" id="readName"><span></span></label></div>
        <div class="field" id="templateField" style="margin-top:10px"><label>נוסח ההקראה</label><input type="text" id="template"><small>{user} = שם הצופה, {text} = ההודעה</small></div>
        <div class="field" style="margin-top:10px"><label>טקסט לבדיקה</label><input type="text" id="testText" value="בדיקה, אחת שתיים. ככה ישמעו ההודעות בשידור!"></div>
        <div class="actions" style="margin-top:12px"><button class="primary" id="hearBtn">▶ השמע כאן</button><button id="obsTestBtn">שלח בדיקה ל-OBS</button></div>
      </div>
    </div>

    <div class="tab" id="tab-chat"><div class="card"><h2>הקראה מהצ'אט</h2>
      <div class="row" style="padding-top:0"><div class="lbl">הקראת הודעות צ'אט עם סימן<small>צופה כותב <b><bdi dir="ltr" id="chatExample">!tts</bdi> אחי איזה משחק</b> – וזה מוקרא בשידור, בלי נקודות.</small></div>
        <label class="switch"><input type="checkbox" id="chatEnabled"><span></span></label></div>
      <div class="grid2">
        <div class="field"><label>הסימן</label><input type="text" id="chatPrefix" dir="ltr" maxlength="10" style="width:140px;direction:ltr;text-align:left"><small>חייב להתחיל בסימן כמו ! # . ~</small></div>
        <div class="field"><label>השהיה לכל צופה (שניות)</label><input type="number" class="num" id="chatCooldown" min="0" max="3600"><small>אתה והמודים – בלי השהיה</small></div>
      </div>
      <div class="field" style="margin-top:12px"><label>מי יכול להשתמש</label>
        <div class="chips" id="chatWho"><button class="chip" data-who="all">כולם</button><button class="chip" data-who="subs">סאבים ומעלה</button>
          <button class="chip" data-who="vip">VIP ומודים</button><button class="chip" data-who="mods">רק מודים</button></div></div>
      <p class="muted" style="margin:12px 0 0">הודעה עם מילה חסומה או קישור פשוט לא מוקראת. המתג הראשי בסקירה מכבה גם את הצ'אט.</p>
    </div>
    <div class="card"><h2>🩺 בדיקת חיבור לצ'אט</h2>
      <div class="svc"><span class="dot" id="cdSub"></span><span class="n">רישום להודעות צ'אט בקיק</span><span class="v" id="cdSubV"></span></div>
      <div class="svc"><span class="dot" id="cdHook"></span><span class="n">התראה אחרונה שהגיעה מקיק</span><span class="v" id="cdHookV"></span></div>
      <div class="svc"><span class="dot" id="cdLast"></span><span class="n">הודעת הצ'אט האחרונה עם סימן</span><span class="v" id="cdLastV"></span></div>
      <p class="muted" id="cdHint" style="margin:10px 0"></p>
      <div class="actions"><button id="chatResync">🔄 הירשם מחדש להודעות צ'אט</button></div>
    </div></div>

    <div class="tab" id="tab-alerts">
      <div class="card"><h2>🔔 הקראת אירועים</h2>
        <p class="muted" style="margin:0">ההתראה על המסך נשארת ב-Botrix – כאן רק הקול. ההקראות נכנסות לאותו תור של הפרסים והצ'אט, בלי התנגשויות.
        כפתור <b>🧪 בדיקה</b> שולח אירוע מדומה ושומעים אותו מיד (גם כשהמתג כבוי). בבדיקה Botrix לא יקפיץ התראה – הוא מגיב רק לאירועים אמיתיים.</p>
        <p class="notice hidden" id="kicksRelogin">בשביל Kicks קיק צריך הרשאה נוספת: <a href="#settings">התנתק</a> והתחבר שוב לאתר פעם אחת.</p>
        <div class="row"><div class="lbl">להציג גם את התיבה של ה-TTS ב-OBS<small>כבוי = רק קול, כדי שלא תופיע תיבה על ההתראה של Botrix.</small></div>
          <label class="switch"><input type="checkbox" id="alertShowCard"><span></span></label></div>
        <p class="muted" id="alertLast" style="margin:10px 0 0"></p>
      </div>
      <div class="card"><div class="row" style="padding-top:0"><div class="lbl"><b>💚 Kicks</b> <span class="dot" id="as-kicks"></span><small id="asV-kicks">כשצופה שולח Kicks – מקריאים את ההודעה שלו.</small></div>
        <div class="actions"><button id="at-kicks">🧪 בדיקה</button><label class="switch"><input type="checkbox" id="alertKicks"><span></span></label></div></div>
        <div class="grid2"><div class="field"><label>מינימום Kicks להקראה</label><input type="number" class="num" id="alertKicksMin" min="1"></div><div class="field"><label>מה יוקרא</label><input type="text" id="alertKicksTemplate" maxlength="120"><small>&#123;user&#125; &#123;amount&#125; &#123;text&#125;</small></div></div></div>
      <div class="card"><div class="row" style="padding-top:0"><div class="lbl"><b>➕ עוקב חדש</b> <span class="dot" id="as-follow"></span><small id="asV-follow">תודה בקול לכל עוקב חדש. בשידור עמוס זה יכול להיות הרבה.</small></div>
        <div class="actions"><button id="at-follow">🧪 בדיקה</button><label class="switch"><input type="checkbox" id="alertFollow"><span></span></label></div></div>
        <div class="grid2"><div class="field"><label>מה יוקרא</label><input type="text" id="alertFollowTemplate" maxlength="120"><small>&#123;user&#125;</small></div></div></div>
      <div class="card"><div class="row" style="padding-top:0"><div class="lbl"><b>⭐ סאב (חדש וחידוש)</b> <span class="dot" id="as-sub"></span><small id="asV-sub"></small></div>
        <div class="actions"><button id="at-sub">🧪 בדיקה</button><label class="switch"><input type="checkbox" id="alertSub"><span></span></label></div></div>
        <div class="grid2"><div class="field"><label>מה יוקרא</label><input type="text" id="alertSubTemplate" maxlength="120"><small>&#123;user&#125; &#123;months&#125;</small></div></div></div>
      <div class="card"><div class="row" style="padding-top:0"><div class="lbl"><b>🎁 סאבים במתנה</b> <span class="dot" id="as-gift"></span><small id="asV-gift"></small></div>
        <div class="actions"><button id="at-gift">🧪 בדיקה</button><label class="switch"><input type="checkbox" id="alertGift"><span></span></label></div></div>
        <div class="grid2"><div class="field"><label>מה יוקרא</label><input type="text" id="alertGiftTemplate" maxlength="120"><small>&#123;user&#125; &#123;count&#125;</small></div></div></div>
    </div>

    <div class="tab" id="tab-reward"><div class="card"><h2>פרס בנקודות הערוץ</h2>
      <div class="grid2">
        <div class="field"><label>שם הפרס</label><input type="text" id="rewardTitle" maxlength="50"></div>
        <div class="field"><label>מחיר (נקודות)</label><input type="number" class="num" id="rewardCost" min="1"></div>
      </div>
      <div class="field" style="margin-top:10px"><label>תיאור לצופים</label><input type="text" id="rewardDescription" maxlength="200"></div>
      <div class="actions" style="margin-top:12px"><button id="rewardBtn">צור פרס בקיק</button></div>
      <p class="muted" style="margin:12px 0 0">הודעה שנחסמת, נדחית או שההקראה שלה נכשלה – מחזירה לצופה את הנקודות.</p>
    </div></div>

    <div class="tab" id="tab-filter"><div class="card"><h2>סינון ושמירה על השידור</h2>
      <div class="row" style="padding-top:0"><div class="lbl">אישור ידני לפני הקראה<small>כל הודעה מחכה בתור עד שתלחץ "הקרא" או "דחה".</small></div>
        <label class="switch"><input type="checkbox" id="manualApproval"><span></span></label></div>
      <div class="row"><div class="lbl">חסימת קישורים</div><label class="switch"><input type="checkbox" id="blockLinks"><span></span></label></div>
      <div class="grid2">
        <div class="field"><label>אורך מקסימלי (תווים)</label><input type="number" class="num" id="maxChars" min="10" max="400"></div>
        <div class="field"><label>הפסקה בין הקראות (שניות)</label><input type="number" class="num" id="gapSeconds" min="0" max="60"></div>
        <div class="field"><label>בקשה ישנה מפרס נדחית אחרי (דקות)</label><input type="number" class="num" id="maxAgeMinutes" min="1" max="1440"></div>
      </div>
      <div class="field" style="margin-top:12px"><label>מילים חסומות (מילה או ביטוי בכל שורה)</label><textarea id="banned"></textarea>
        <small>נתפס גם עם ו/ה/ש/ב/כ/ל/מ בהתחלה.</small></div>
    </div></div>

    <div class="savebar" id="saveBar"><button class="primary" id="saveBtn">שמור הגדרות</button><span class="muted" id="saved"></span></div>
  </section>

  <!-- ═══════ Now Playing ═══════ -->
  <section class="page" id="page-np">
    <h1>🎵 Now Playing</h1><p class="sub">השיר שמתנגן אצלך בספוטיפיי – על המסך בשידור, עם 7 עיצובים.</p>
    <p class="notice hidden" id="npError"></p>
    <div class="card"><h2>1. חיבור לספוטיפיי</h2>
      <div class="row" style="padding-top:0"><div class="lbl"><span class="dot" id="npDot"></span> <span id="npStatus"></span><small id="npStatusHint"></small></div>
        <div class="actions"><button class="primary" id="npConnect">התחבר לספוטיפיי</button><button class="danger hidden" id="npDisconnect">נתק</button></div></div>
      <details id="npSetup"><summary><b>הגדרה חד-פעמית (5 דקות)</b> – יוצרים אפליקציה קטנה בספוטיפיי</summary>
        <ol class="muted" style="line-height:1.9">
          <li>נכנסים ל-<a href="https://developer.spotify.com/dashboard" target="_blank" rel="noopener">developer.spotify.com/dashboard</a> ומתחברים עם חשבון הספוטיפיי שלך.</li>
          <li>לוחצים <b>Create app</b>. שם ותיאור – מה שבא לך (למשל Catego).</li>
          <li>ב-<b>Redirect URIs</b> מדביקים את הכתובת הזו ולוחצים Add:
            <div class="url" style="margin:6px 0"><input type="text" id="npRedirect" readonly dir="ltr"><button id="npCopyRedirect">העתק</button></div></li>
          <li>ב-<b>Which API/SDKs</b> מסמנים <b>Web API</b>, מאשרים את התנאים ושומרים.</li>
          <li>בדף האפליקציה ← <b>Settings</b> ← מעתיקים את ה-<b>Client ID</b> ומדביקים כאן למטה. (את ה-Client Secret <b>לא</b> צריך – אל תדביק אותו.)</li>
        </ol>
        <div class="field"><label>Client ID</label><div class="url"><input type="text" id="npClientId" dir="ltr" maxlength="40" placeholder="32 תווים" style="direction:ltr;text-align:left"><button class="primary" id="npSaveClient">שמור</button></div></div>
      </details>
    </div>
    <div class="card"><h2>2. הקישור ל-OBS</h2>
      <div class="row" style="padding-top:0"><div class="lbl"><span class="dot" id="npWDot"></span> <span id="npWText"></span></div></div>
      <div class="url"><input type="text" id="npUrl" readonly><button id="npCopy">העתק</button></div>
      <p class="muted" style="margin-bottom:0">ב-OBS: Sources ← <b>+</b> ← <b>Browser</b> ← מדביקים, רוחב <b>520</b> וגובה <b>340</b>. הווידג'ט מתעדכן לבד כשמחליפים כאן עיצוב.</p>
    </div>
    <div class="card"><h2>3. עיצוב</h2>
      <div class="chips" id="npThemes">
        <button class="chip" data-theme="classic">Classic<small>כרטיס כהה</small></button>
        <button class="chip" data-theme="minimal">Minimal<small>בלי רקע</small></button>
        <button class="chip" data-theme="glass">Glass<small>זכוכית</small></button>
        <button class="chip" data-theme="neon">Neon<small>זוהר</small></button>
        <button class="chip" data-theme="album">Album<small>עטיפה גדולה</small></button>
        <button class="chip" data-theme="slim">Slim<small>פס דק</small></button>
        <button class="chip" data-theme="vinyl">Vinyl<small>תקליט מסתובב</small></button>
      </div>
      <div class="grid2" style="margin-top:12px">
        <div class="field"><label>צבע</label><input type="color" id="npAccent" style="width:70px;height:36px;padding:2px"></div>
        <div class="field"><label>שקיפות רקע: <span id="npOpacityV"></span></label><input type="range" id="npOpacity" min="15" max="100" step="5"></div>
        <div class="field"><label>גודל: <span id="npScaleV"></span></label><input type="range" id="npScale" min="60" max="130" step="5"></div>
      </div>
      <div class="row"><div class="lbl">להסתיר כשהשיר בהשהיה</div><label class="switch"><input type="checkbox" id="npHidePaused"><span></span></label></div>
      <div class="row"><div class="lbl">להקטין אחרי 5 שניות<small>מציג גדול כשמתחלף שיר, ואז עובר למצב קומפקטי.</small></div><label class="switch"><input type="checkbox" id="npCompact"><span></span></label></div>
      <h2 style="margin-top:14px">תצוגה מקדימה</h2>
      <div style="background:repeating-conic-gradient(#2a2f35 0 25%,#1f2328 0 50%) 0 0/24px 24px;border-radius:12px;height:340px;overflow:hidden">
        <iframe id="npPreview" title="תצוגה מקדימה" style="width:520px;max-width:100%;height:340px;border:0;background:transparent" allowtransparency="true"></iframe></div>
    </div>
  </section>

  <!-- ═══════ OBS ═══════ -->
  <section class="page" id="page-obs">
    <h1>מקורות OBS</h1><p class="sub">כל ווידג'ט הוא Browser Source ב-OBS. הקישורים אישיים – אל תשתף אותם.</p>
    <div class="card"><h2>🔊 הקראה (TTS)</h2>
      <div class="row" style="padding-top:0"><div class="lbl"><span class="dot" id="obsDot"></span> <span id="obsText"></span></div></div>
      <div class="url"><input type="text" id="obsUrl" readonly><button id="copyBtn">העתק</button><button class="ghost" id="previewBtn">תצוגה מקדימה</button></div>
      <p class="muted" style="margin-bottom:0">ב-OBS: Sources ← <b>+</b> ← <b>Browser</b> ← מדביקים את הקישור, רוחב <b>1920</b> וגובה <b>400</b>. הכרטיס מופיע רק בזמן הקראה.</p>
    </div>
    <div class="card"><h2>🎵 Now Playing</h2>
      <div class="row" style="padding-top:0"><div class="lbl"><span class="dot" id="npObsDot"></span> <span id="npObsText"></span></div><a href="#np">להגדרות ←</a></div>
      <div class="url"><input type="text" id="npUrl2" readonly><button id="npCopy2">העתק</button></div>
      <p class="muted" style="margin-bottom:0">Browser Source ברוחב <b>520</b> וגובה <b>340</b>.</p>
    </div>
    <div class="card soon-card"><span class="ic">🔔</span><div><b>התראות</b><div class="muted">עוקבים, סאבים ו-Kicks – בקרוב.</div></div></div>
  </section>

  <!-- ═══════ הגדרות ═══════ -->
  <section class="page" id="page-settings">
    <h1>הגדרות</h1><p class="sub">החשבון והקישורים שלך.</p>
    <div class="card"><h2>חשבון</h2>
      <div class="row" style="padding-top:0"><div class="lbl">מחובר לקיק בתור <b id="setWho"></b><small>ההרשאות: פרטי משתמש וערוץ, פרסי נקודות, והתראות (פרסים וצ'אט).</small></div>
        <form method="post" action="/auth/logout"><button class="danger" type="submit">התנתק</button></form></div>
    </div>
    <div class="card"><h2>קישורי הווידג'טים</h2>
      <div class="row" style="padding-top:0"><div class="lbl">יצירת קישור חדש<small>הקישור הישן יפסיק לעבוד מיד – צריך להדביק את החדש ב-OBS. שימושי אם הקישור דלף.</small></div>
        <button class="danger" id="rotateBtn">צור קישור חדש</button></div>
    </div>
  </section>
</main>
</div>
<div id="toast"></div>`;

// הקוד שרץ בדפדפן. כתוב בלי ${…} כדי שלא יתנגש עם התבנית של השרת.
const SCRIPT = String.raw`
const $=id=>document.getElementById(id);
let state=null,formLoaded=false,voice="he-IL-HilaNeural",gvoice="Kore",mode="edge",chatWho="all",speakerWs=null,speakerOn=false;
const STATUS={playing:"🔊 מוקרא עכשיו",waiting:"בתור",approval:"ממתין לאישור",done:"הוקרא",skipped:"דולג",rejected:"נדחה",failed:"נכשל"};
function toast(t,bad){const e=$("toast");e.textContent=t;e.className="show"+(bad?" bad":"");clearTimeout(e._t);e._t=setTimeout(()=>e.className="",3500);}
async function post(op,body={}){const r=await fetch("/api/"+op,{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
 if(r.status===401){location.href="/";return;}const ct=r.headers.get("Content-Type")||"";if(ct.includes("audio"))return r.blob();
 const j=await r.json().catch(()=>({}));if(!r.ok)throw Error(j.error||"הפעולה נכשלה");if(j.state)render(j.state);return j;}
function act(id,fn){const b=$(id);b.onclick=async()=>{b.disabled=true;try{await fn();}catch(e){toast(e.message,true);}finally{b.disabled=false;}};}

// ── ניווט ──
function go(){const p=(location.hash||"#overview").slice(1);const page=document.getElementById("page-"+p)?p:"overview";
 document.querySelectorAll(".page").forEach(x=>x.classList.toggle("sel",x.id==="page-"+page));
 document.querySelectorAll("nav a[data-page]").forEach(a=>a.classList.toggle("sel",a.dataset.page===page));window.scrollTo(0,0);}
window.addEventListener("hashchange",go);go();
document.querySelectorAll("#ttsTabs button").forEach(b=>b.onclick=()=>{document.querySelectorAll("#ttsTabs button").forEach(x=>x.classList.toggle("sel",x===b));
 document.querySelectorAll("#page-tts .tab").forEach(t=>t.classList.toggle("sel",t.id==="tab-"+b.dataset.tab));$("saveBar").classList.toggle("hidden",b.dataset.tab==="queue");});
$("saveBar").classList.add("hidden");

// ── בחירות ──
function pick(sel,attr,val){document.querySelectorAll(sel).forEach(c=>c.classList.toggle("sel",c.dataset[attr]===val));}
function setVoice(v){voice=v;pick("#voices .chip","voice",v);}
function setWho(w){chatWho=w;pick("#chatWho .chip","who",w);}
function setGVoice(v){gvoice=v;pick("#gvoices .chip","gvoice",v);}
function setMode(m){mode=m;pick("#modes .chip","mode",m);$("edgePanel").classList.toggle("hidden",m!=="edge");$("serverPanel").classList.toggle("hidden",m==="edge");
 $("voices").classList.toggle("hidden",m==="gemini");$("gvoices").classList.toggle("hidden",m!=="gemini");updateServerOff();}
function updateServerOff(){const st=state?.settings;let msg="";
 if(mode==="gemini"&&state&&!state.geminiTts)msg="Gemini עוד לא מוגדר בשרת – צריך להוסיף מפתח (GEMINI_API_KEY).";
 if(mode==="server"&&state&&!state.serverTts)msg="Azure עוד לא מוגדר בשרת – צריך להוסיף מפתח Azure.";
 $("serverOff").textContent=msg;$("serverOff").classList.toggle("hidden",!msg);}
document.querySelectorAll("#gvoices .chip").forEach(c=>c.onclick=()=>setGVoice(c.dataset.gvoice));
document.querySelectorAll("#voices .chip").forEach(c=>c.onclick=()=>setVoice(c.dataset.voice));
document.querySelectorAll("#chatWho .chip").forEach(c=>c.onclick=()=>setWho(c.dataset.who));
document.querySelectorAll("#modes .chip").forEach(c=>c.onclick=async()=>{const m=c.dataset.mode;
 if(m==="server"&&state&&!state.serverTts){toast("Azure עוד לא הוגדר בשרת",true);return;}
 if(m==="gemini"&&state&&!state.geminiTts){toast("Gemini עוד לא הוגדר בשרת",true);return;}
 setMode(m);try{await post("settings",{settings:{playback:m}});toast(m==="edge"?"הקול יוצא מהלשונית ב-Edge":m==="gemini"?"הקול יוצא מהשרת (Gemini) דרך הווידג'ט ב-OBS":"הקול יוצא מהשרת (Azure) דרך הווידג'ט ב-OBS");}catch(e){toast(e.message,true);}});
function showRange(){const r=+$("rate").value;$("rateV").textContent=r===0?"רגילה":(r>0?"+":"")+r+"%";$("volumeV").textContent=$("volume").value+"%";}
$("rate").oninput=$("volume").oninput=showRange;
$("chatPrefix").oninput=()=>{$("chatExample").textContent=$("chatPrefix").value.trim()||"!tts";};
const ltr=t=>"⁦"+t+"⁩"; // שומר על "!tts" בכיוון הנכון בתוך טקסט עברי

const FIELDS=["rewardTitle","rewardCost","rewardDescription","template","maxChars","gapSeconds","maxAgeMinutes","rate","volume","chatPrefix","chatCooldown",
 "alertKicksMin","alertKicksTemplate","alertFollowTemplate","alertSubTemplate","alertGiftTemplate"];
const ALERTS=[["kicks","Kicks"],["follow","Follow"],["sub","Sub"],["gift","Gift"]];
function loadForm(s){for(const k of FIELDS)$(k).value=s[k];$("manualApproval").checked=s.manualApproval;$("blockLinks").checked=s.blockLinks;
 $("chatEnabled").checked=!!s.chatEnabled;$("readName").checked=s.readName!==false;$("templateField").classList.toggle("hidden",s.readName===false);for(const [,K] of ALERTS)$("alert"+K).checked=!!s["alert"+K];$("alertShowCard").checked=!!s.alertShowCard;$("banned").value=(s.banned||[]).join("\n");setVoice(s.voice);setGVoice(s.geminiVoice||"Kore");setWho(s.chatWho||"all");setMode(s.playback||"edge");showRange();$("chatPrefix").oninput();}
function collect(){return{rewardTitle:$("rewardTitle").value,rewardCost:+$("rewardCost").value,rewardDescription:$("rewardDescription").value,template:$("template").value,
 maxChars:+$("maxChars").value,gapSeconds:+$("gapSeconds").value,maxAgeMinutes:+$("maxAgeMinutes").value,rate:+$("rate").value,volume:+$("volume").value,
 readName:$("readName").checked,manualApproval:$("manualApproval").checked,blockLinks:$("blockLinks").checked,voice,geminiVoice:gvoice,playback:mode,chatEnabled:$("chatEnabled").checked,
 chatPrefix:$("chatPrefix").value.trim(),chatCooldown:+$("chatCooldown").value,
 alertKicksMin:+$("alertKicksMin").value,alertKicksTemplate:$("alertKicksTemplate").value,alertFollowTemplate:$("alertFollowTemplate").value,
 alertSubTemplate:$("alertSubTemplate").value,alertGiftTemplate:$("alertGiftTemplate").value,
 alertShowCard:$("alertShowCard").checked,alertKicks:$("alertKicks").checked,alertFollow:$("alertFollow").checked,alertSub:$("alertSub").checked,alertGift:$("alertGift").checked,chatWho,banned:$("banned").value.split(/\n|,/).map(x=>x.trim()).filter(Boolean)};}

function svc(id,cls,text){$(id).className="dot "+cls;$(id+"V").textContent=text;}

function render(s){state=s;const st=s.settings;if(!formLoaded){loadForm(st);formLoaded=true;}
 const name=s.user?.name||"";$("who").textContent=name;$("setWho").textContent=name;$("chanName").textContent=name;
 $("chanLink").textContent="kick.com/"+(s.user?.slug||name.toLowerCase());
 const ch=s.channel;$("chanLive").className="live"+(ch&&ch.live?" on":"");$("chanLive").textContent=ch&&ch.live?"בלייב · "+ch.viewers+" צופים":"לא בלייב";
 $("ovSub").textContent=ch&&ch.live?("בלייב עכשיו"+(ch.category?" · "+ch.category:"")+(ch.title?" · "+ch.title:"")):"כל מה שקורה בערוץ במבט אחד.";
 const open=s.items.filter(i=>["playing","waiting","approval"].includes(i.status)).length;
 $("stTotal").textContent=s.stats.total;$("stChat").textContent=s.stats.chat;$("stReward").textContent=s.stats.reward;$("stQueue").textContent=open;
 $("enabled").checked=st.enabled;
 let title,text,dot="";
 if(!s.connected){title="החיבור לקיק פג";text="התחבר מחדש כדי שההקראה תמשיך לעבוד.";dot="warn";}
 else if(st.enabled){title=s.paused?"ההקראה פעילה · התור מושהה":"ההקראה פעילה 🔊";dot="on";
  const parts=[];if(s.rewardId)parts.push("פרס \""+st.rewardTitle+"\" ב-"+st.rewardCost.toLocaleString()+" נקודות");if(st.chatEnabled)parts.push("צ'אט עם "+ltr(st.chatPrefix));
  text=parts.length?"צופים יכולים להקריא דרך: "+parts.join(" · "):"אין עדיין דרך לצופים להקריא – הפעל צ'אט או צור פרס.";}
 else{title="ההקראה כבויה";text="הדלק את המתג כדי שצופים יוכלו להקריא (הפרס בקיק מושהה עד אז).";}
 $("statusTitle").textContent=title;$("statusText").textContent=text;$("dot").className="dot "+dot;
 $("error").textContent=s.error||"";$("error").classList.toggle("hidden",!s.error);
 $("reloginBanner").classList.toggle("hidden",s.connected&&s.rewardsScope);
 $("reloginText").textContent=!s.connected?"החיבור לקיק פג – צריך להתחבר מחדש.":"ההתחברות נעשתה בלי כל ההרשאות – התחבר מחדש.";
 $("qaPause").textContent=s.paused?"▶ המשך תור":"⏸ השהה תור";$("qaSkip").disabled=!s.playingId;
 $("rewardBtn").textContent=s.rewardId?"עדכן את הפרס בקיק":"צור פרס בקיק";
 // שירותים
 svc("svKick",s.connected?"on":"bad",s.connected?"מחובר בתור "+name:"לא מחובר");
 const hookOk=s.lastHookAt&&Date.now()-s.lastHookAt<864e5;
 const ago=t=>{const m=Math.round((Date.now()-t)/60000);return m<1?"עכשיו":m<60?"לפני "+m+" דק'":"לפני "+Math.round(m/60)+" שע'";};
 svc("svHook",hookOk?"on":(s.rewardId?"warn":""),hookOk?"מחובר · התראה אחרונה "+ago(s.lastHookAt):s.badHookAt?"מגיעות התראות עם חתימה לא תקינה":s.webhook?"עוד לא הגיעה אף התראה מקיק":(s.rewardId?"בדיקה כל כמה שניות":"עוד אין פרס"));
 svc("svChat",st.chatEnabled?(s.chatSubscribed?"on":"warn"):"",st.chatEnabled?(s.chatSubscribed?"מאזין ל-"+ltr(st.chatPrefix):"לא מחובר"):"כבוי");
 svc("svObs",s.overlays?"on":"warn",s.overlays?s.overlays+" מחובר":"לא פתוח – הוסף ב\"מקורות OBS\"");
 if(st.playback==="gemini"){$("svVoiceN").textContent="קולות Gemini";svc("svVoice",s.geminiTts?"on":"bad",s.geminiTts?"מוכן · "+st.geminiVoice:"לא מוגדר");}
 else if(st.playback==="server"){$("svVoiceN").textContent="קולות Azure";svc("svVoice",s.serverTts?"on":"bad",s.serverTts?"מוכן":"לא מוגדר");}
 else{$("svVoiceN").textContent="לשונית מקריאה";svc("svVoice",(speakerOn||s.speakers)?"on":"warn",speakerOn?"הלשונית הזו":(s.speakers?"לשונית אחרת":"אף לשונית"));}
 const needSpeaker=st.playback==="edge"&&!speakerOn&&!s.speakers&&st.enabled;$("spkBanner").classList.toggle("hidden",!needSpeaker);
 // דפדפן בלי קולות עבריים (Chrome וכו'): להציע את Gemini במקום
 const noVoices=!heVoices().length;$("spkBannerBtn").classList.toggle("hidden",noVoices);$("spkBannerGemini").classList.toggle("hidden",!noVoices||!s.geminiTts);
 $("spkBannerText").textContent=noVoices?(s.geminiTts?"בדפדפן הזה אין קולות עבריים. פתח את האתר ב-Edge כדי להקריא מכאן, או עבור ל-Gemini (הקול יוצא ישר מ-OBS).":"בדפדפן הזה אין קולות עבריים – פתח את האתר ב-Microsoft Edge והפעל שם הקראה."):"אף לשונית לא מקריאה כרגע – ההקראות מחכות.";
 speakerStatus();updateServerOff();renderNp(s);renderChatDiag(s);renderAlerts(s);
 // OBS
 $("obsUrl").value=location.origin+s.overlayPath;$("obsDot").className="dot "+(s.overlays?"on":"warn");
 $("obsText").textContent=s.overlays?"פתוח ב-OBS ✓":"לא פתוח ב-OBS כרגע";
 // תור
 $("queueSummary").textContent=open?open+" ממתינים":"התור ריק";const q=$("queue");q.replaceChildren();
 if(!s.items.length){const p=document.createElement("p");p.className="muted";p.style.margin="0";p.textContent="עוד אין הודעות. ברגע שצופה יקנה את הפרס או יכתוב בצ'אט – ההודעה תופיע כאן.";q.append(p);}
 for(const i of s.items){const isOpen=["playing","waiting","approval"].includes(i.status);const row=document.createElement("div");row.className="q"+(isOpen?"":" old");
  const body=document.createElement("div");body.className="body";const top=document.createElement("div");top.className="top";
  const who=document.createElement("span");who.className="who";who.textContent=(i.alert?"🔔 ":i.chat?"💬 ":i.test?"🧪 ":"🎁 ")+i.user;
  const meta=document.createElement("span");meta.className="meta";
  meta.textContent=new Date(i.at).toLocaleTimeString("he-IL",{hour:"2-digit",minute:"2-digit"})+" · "+(STATUS[i.status]||i.status)+(i.note?" · "+i.note:"");
  top.append(who,meta);const tx=document.createElement("div");tx.className="text";tx.textContent=i.text;body.append(top,tx);row.append(body);
  if(i.status==="approval"){const b=document.createElement("button");b.className="primary";b.textContent="✓ הקרא";b.onclick=()=>post("approve",{id:i.id}).catch(e=>toast(e.message,true));row.append(b);}
  if(i.status==="approval"||i.status==="waiting"){const b=document.createElement("button");b.textContent="✕ דחה";b.onclick=()=>post("reject",{id:i.id}).catch(e=>toast(e.message,true));row.append(b);}
  q.append(row);}}

// ── הקראה מהלשונית (Web Speech של הדפדפן – ב-Edge יש את Hila ו-Avri) ──
const isEdge=navigator.userAgent.includes("Edg/");const hasSpeech="speechSynthesis" in window;
function heVoices(){return hasSpeech?(speechSynthesis.getVoices()||[]).filter(v=>/^(he|iw)/i.test(v.lang)):[];}
const VOICE_RE={"he-IL-AvriNeural":/avri|אברי/i,"he-IL-HilaNeural":/hila|הילה/i};
function pickVoice(id){const want=VOICE_RE[id]||VOICE_RE["he-IL-HilaNeural"];const he=heVoices();
 return he.find(v=>want.test(v.name)||want.test(v.voiceURI))||he.find(v=>/natural|online/i.test(v.name))||he[0]||null;}
const shortName=v=>v?v.name.replace(/^Microsoft\s+/i,"").replace(/\s*-\s*Hebrew.*$/i,""):"";
function speak(text,voiceId,rate,volume,onEnd){if(!hasSpeech){onEnd("no-speech");return null;}
 const v=pickVoice(voiceId);if(!v){onEnd("no-voice");return null;}
 const busy=speechSynthesis.speaking||speechSynthesis.pending;if(busy)speechSynthesis.cancel();
 const u=new SpeechSynthesisUtterance(text);u.voice=v;u.lang=v.lang;u.rate=Math.max(.5,Math.min(2,1+rate/100));u.volume=volume;
 let finished=false;const end=e=>{if(finished)return;finished=true;onEnd(e);};u.onend=()=>end(null);
 u.onerror=ev=>end(ev.error==="interrupted"||ev.error==="canceled"?null:"speech-error");setTimeout(()=>speechSynthesis.speak(u),busy?150:0);return v;}
function speakerStatus(){const he=heVoices();const hila=he.find(v=>VOICE_RE["he-IL-HilaNeural"].test(v.name)),avri=he.find(v=>VOICE_RE["he-IL-AvriNeural"].test(v.name));
 $("spkText").textContent=speakerOn?"✅ הלשונית הזו מקריאה את ההודעות":(state&&state.speakers?"✅ לשונית אחרת מקריאה":"⚠️ אף לשונית לא מקריאה כרגע");
 $("spkHint").textContent=!hasSpeech?"הדפדפן הזה לא יכול להקריא.":!isEdge&&!he.length?"פתח את האתר ב-Microsoft Edge – רק בו יש את Hila ו-Avri.":
  !he.length?"לא נמצאו קולות עבריים בדפדפן הזה.":"קולות שנמצאו: "+he.map(shortName).join(", ")+(hila&&avri?" ✓":" – חסר "+[!hila&&"Hila",!avri&&"Avri"].filter(Boolean).join(" ו-"));
 $("speakerBtn").textContent=speakerOn?"⏹ הפסק להקריא מכאן":"🔊 הפעל הקראה מהלשונית הזו";}
if(hasSpeech)speechSynthesis.onvoiceschanged=()=>{speakerStatus();if(state)render(state);};
let spkRetry=1000;
function connectSpeaker(){speakerWs=new WebSocket((location.protocol==="https:"?"wss://":"ws://")+location.host+"/api/ws?speaker=1");
 speakerWs.onmessage=e=>{spkRetry=1000;let m;try{m=JSON.parse(e.data);}catch{return;}
  if(m.type==="speak"){const id=m.id;speak(m.text,m.voice,m.rate,m.volume,err=>{try{speakerWs.send(JSON.stringify({type:"done",id,error:err||undefined}));}catch{}});}
  else if(m.type==="stop"&&hasSpeech){speechSynthesis.cancel();}};
 speakerWs.onclose=e=>{if(!speakerOn)return;if(e.code===4001){location.href="/";return;}
  if(e.code===4002){speakerOn=false;speakerStatus();if(state)render(state);toast("לשונית אחרת התחילה להקריא – הלשונית הזו הפסיקה");return;}
  setTimeout(connectSpeaker,spkRetry);spkRetry=Math.min(spkRetry*2,15000);};}
function toggleSpeaker(){if(speakerOn){speakerOn=false;try{speakerWs.close();}catch{}if(hasSpeech)speechSynthesis.cancel();speakerStatus();if(state)render(state);return;}
 if(!heVoices().length){toast("בדפדפן הזה אין קולות עבריים – פתח את האתר ב-Microsoft Edge",true);return;}
 const warm=new SpeechSynthesisUtterance(" ");warm.volume=0;speechSynthesis.speak(warm); // הלחיצה "מאשרת" לדפדפן להשמיע קול
 speakerOn=true;connectSpeaker();speakerStatus();if(state)render(state);toast("הלשונית הזו תקריא את ההודעות – השאר אותה פתוחה");}
$("speakerBtn").onclick=toggleSpeaker;$("spkBannerBtn").onclick=toggleSpeaker;
act("spkBannerGemini",async()=>{setMode("gemini");await post("settings",{settings:{playback:"gemini"}});toast("עברת ל-Gemini – הקול יוצא עכשיו מהווידג'ט ב-OBS");});
window.addEventListener("beforeunload",e=>{if(speakerOn){e.preventDefault();e.returnValue="";}});

// ── פעולות ──
$("enabled").onchange=async e=>{try{await post("enable",{enabled:e.target.checked});}catch(err){toast(err.message,true);e.target.checked=!e.target.checked;}};
$("chatEnabled").onchange=async e=>{try{await post("settings",{settings:{chatEnabled:e.target.checked,chatPrefix:$("chatPrefix").value.trim(),chatWho,chatCooldown:+$("chatCooldown").value}});
 toast(e.target.checked?"הקראה מהצ'אט הופעלה – כתבו "+ltr($("chatPrefix").value.trim())+" ואחריו הודעה":"הקראה מהצ'אט כובתה");}catch(err){toast(err.message,true);e.target.checked=!e.target.checked;}};
act("saveBtn",async()=>{await post("settings",{settings:collect()});$("saved").textContent="נשמר ב-"+new Date().toLocaleTimeString("he-IL");formLoaded=false;render(state);});
act("rewardBtn",async()=>{await post("settings",{settings:collect()});await post("reward");toast("✓ הפרס מוכן בקיק");});
act("qaSkip",()=>post("skip"));act("qaPause",()=>post("pause",{paused:!state?.paused}));
act("qaClear",async()=>{if(!confirm("לנקות את כל התור? בקשות מפרסים יידחו והנקודות יחזרו לצופים."))return;await post("clear");toast("התור נוקה");});
async function sendTest(){await post("test",{text:$("testText").value||"בדיקת הקראה"});
 toast(mode==="edge"?((speakerOn||state?.speakers)?"נשלח – תשמע מהלשונית ותראה ב-OBS":"נשמר – יוקרא כשתפעיל הקראה מהלשונית"):(state?.overlays?"נשלח ל-OBS":"נשמר – יושמע כשהווידג'ט ייפתח ב-OBS"));}
act("qaTest",sendTest);act("obsTestBtn",async()=>{await post("settings",{settings:collect()});await sendTest();});
act("hearBtn",async()=>{if(mode==="edge"){await new Promise(res=>{const used=speak($("testText").value||"בדיקת קול",voice,+$("rate").value,+$("volume").value/100,err=>{
  if(err)toast(err==="no-voice"?"לא נמצא קול עברי בדפדפן – פתח ב-Edge":"ההקראה נכשלה ("+err+")",true);res();});if(used)toast("מקריא בקול: "+shortName(used));});return;}
 toast("מכין את הקול…");const blob=await post("voice-test",{text:$("testText").value,voice:mode==="gemini"?gvoice:voice,rate:+$("rate").value});const a=new Audio(URL.createObjectURL(blob));a.volume=+$("volume").value/100;await a.play();});
act("copyBtn",async()=>{await navigator.clipboard.writeText($("obsUrl").value);toast("הקישור הועתק ✓ – הדבק אותו ב-OBS");});
$("previewBtn").onclick=()=>window.open($("obsUrl").value+"?demo=1","_blank");
act("rotateBtn",async()=>{if(!confirm("ליצור קישור חדש? הקישור הישן יפסיק לעבוד, וצריך להדביק את החדש ב-OBS."))return;await post("rotate");toast("נוצר קישור חדש – עדכן אותו ב-OBS");});

// ── בדיקת צ'אט ──
function renderChatDiag(s){const st=s.settings,ago=t=>{const m=Math.round((Date.now()-t)/60000);return m<1?"עכשיו":m<60?"לפני "+m+" דק'":m<1440?"לפני "+Math.round(m/60)+" שע'":"לפני "+Math.round(m/1440)+" ימים";};
 svc("cdSub",!st.chatEnabled?"":s.chatSubscribed?"on":"warn",!st.chatEnabled?"הקראה מהצ'אט כבויה":s.chatSubscribed?"רשום ✓":"לא רשום – לחץ על הירשם מחדש");
 svc("cdHook",s.lastHookAt?"on":s.badHookAt?"warn":"warn",s.lastHookAt?ago(s.lastHookAt):s.badHookAt?"הגיעו עם חתימה לא תקינה":"אף פעם");
 const c=s.chatDebug;svc("cdLast",c?(c.reason.includes("✓")?"on":"warn"):"",c?(c.user+": "+c.text+" ← "+c.reason+" ("+ago(c.at)+")"):"עוד לא הגיעה");
 $("cdHint").textContent=!s.lastHookAt?(s.badHookAt?"קיק שולח, אבל החתימה לא עוברת בדיקה – תגיד לי, זה צד שלי.":"קיק עוד לא שלח לאתר אף התראה. בדוק בהגדרות האפליקציה בקיק (Settings ← Developer) ש-Enable Webhooks מופעל וה-Webhook URL הוא "+location.origin+"/kick/webhook – ושמרת."):
  c?"":"מגיעות התראות מקיק. כתוב בצ'אט "+ltr(st.chatPrefix)+" בדיקה ותראה כאן מה קרה.";}
act("chatResync",async()=>{await post("chat-resync");toast("נרשמת מחדש להודעות צ'אט ✓");});

$("readName").onchange=async e=>{$("templateField").classList.toggle("hidden",!e.target.checked);
 try{await post("settings",{settings:{readName:e.target.checked}});toast(e.target.checked?"השם של הכותב יוקרא לפני ההודעה":"מעכשיו מוקראת רק ההודעה, בלי השם");}catch(err){toast(err.message,true);e.target.checked=!e.target.checked;}};
// ── אירועים ──
$("alertShowCard").onchange=async e=>{try{await post("settings",{settings:{alertShowCard:e.target.checked}});toast(e.target.checked?"באירועים תופיע גם התיבה":"באירועים – רק קול");}catch(err){toast(err.message,true);e.target.checked=!e.target.checked;}};
const KIND_NAME={kicks:"Kicks",follow:"עוקב",sub:"סאב",gift:"סאבים במתנה"};
function renderAlerts(s){const st=s.settings;
 for(const [k,K] of ALERTS){const on=!!st["alert"+K],sub=(s.alertSubs||{})[k];
  if(!on){$("as-"+k).className="dot";continue;}
  $("as-"+k).className="dot "+(sub===true?"on":"warn");$("asV-"+k).textContent=sub===true?"פעיל ✓":sub?String(sub):"מתחבר…";}
 $("kicksRelogin").classList.toggle("hidden",!!s.kicksScope);
 const a=s.alertDebug;$("alertLast").textContent=a?"אחרון: "+(KIND_NAME[a.kind]||a.kind)+" · "+a.user+" ← "+a.reason:"";}
for(const [k,K] of ALERTS){
 $("alert"+K).onchange=async e=>{try{await post("settings",{settings:collect()});toast(e.target.checked?"הקראת "+KIND_NAME[k]+" הופעלה":"הקראת "+KIND_NAME[k]+" כובתה");}catch(err){toast(err.message,true);e.target.checked=!e.target.checked;}};
 act("at-"+k,async()=>{await post("settings",{settings:collect()});await post("alert-test",{kind:k});
  toast(mode==="edge"?((speakerOn||state?.speakers)?"בדיקה נשלחה – תשמע עכשיו":"נשמר – יוקרא כשתפעיל הקראה מהלשונית"):(state?.overlays?"בדיקה נשלחה ל-OBS":"נשמר – יושמע כשהווידג'ט ייפתח ב-OBS"));});}

// ── Now Playing ──
let npTheme="classic",npLoaded=false,npTimer=null,npPrevKey="";
function npCollect(){return{theme:npTheme,accent:$("npAccent").value,opacity:+$("npOpacity").value/100,scale:+$("npScale").value/100,hidePaused:$("npHidePaused").checked,compact:$("npCompact").checked};}
function npShowRange(){$("npOpacityV").textContent=$("npOpacity").value+"%";$("npScaleV").textContent=$("npScale").value+"%";}
function npPreview(){if(!state)return;const c=npCollect();const q=new URLSearchParams({demo:"1",theme:c.theme,accent:c.accent,opacity:String(c.opacity),scale:String(c.scale)});
 const src=state.np.path+"?"+q;if(src!==npPrevKey){npPrevKey=src;$("npPreview").src=src;}}
function npSave(){npShowRange();npPreview();clearTimeout(npTimer);npTimer=setTimeout(()=>post("np-settings",{settings:npCollect()}).catch(e=>toast(e.message,true)),400);}
function npLoad(s){$("npAccent").value=s.accent;$("npOpacity").value=Math.round(s.opacity*100);$("npScale").value=Math.round(s.scale*100);
 $("npHidePaused").checked=s.hidePaused;$("npCompact").checked=s.compact;npTheme=s.theme;pick("#npThemes .chip","theme",s.theme);npShowRange();}
document.querySelectorAll("#npThemes .chip").forEach(c=>c.onclick=()=>{npTheme=c.dataset.theme;pick("#npThemes .chip","theme",npTheme);npSave();});
for(const id of ["npAccent","npOpacity","npScale"])$(id).oninput=npSave;
for(const id of ["npHidePaused","npCompact"])$(id).onchange=npSave;
function renderNp(s){const n=s.np;if(!n)return;if(!npLoaded){npLoad(n.settings);$("npClientId").value=n.clientId||"";npLoaded=true;}
 $("npRedirect").value=location.origin+"/spotify/callback";
 const url=location.origin+n.path;$("npUrl").value=url;$("npUrl2").value=url;
 $("npDot").className="dot "+(n.connected?"on":"warn");
 $("npStatus").textContent=n.connected?"מחובר לספוטיפיי ✓":n.clientId?"עוד לא מחובר":"צריך הגדרה חד-פעמית";
 $("npStatusHint").textContent=n.connected?"השיר שמתנגן יופיע ב-OBS.":n.clientId?"לחץ \"התחבר לספוטיפיי\" ואשר.":"פתח את ההגדרה למטה – יוצרים אפליקציה בספוטיפיי ומדביקים Client ID.";
 $("npConnect").classList.toggle("hidden",n.connected);$("npDisconnect").classList.toggle("hidden",!n.connected);
 if(!n.clientId)$("npSetup").open=true;
 $("npError").textContent=n.error||"";$("npError").classList.toggle("hidden",!n.error);
 const w=n.widgets?"פתוח ב-OBS ✓":"לא פתוח ב-OBS כרגע";$("npWText").textContent=w;$("npObsText").textContent=w;
 $("npWDot").className=$("npObsDot").className="dot "+(n.widgets?"on":"warn");npPreview();}
act("npSaveClient",async()=>{await post("spotify-client",{clientId:$("npClientId").value.trim()});toast("נשמר ✓ – עכשיו לחץ \"התחבר לספוטיפיי\"");});
$("npConnect").onclick=()=>{if(!state?.np?.clientId){$("npSetup").open=true;toast("קודם מדביקים Client ID (בהגדרה למטה)",true);return;}location.href="/spotify/login";};
act("npDisconnect",async()=>{if(!confirm("לנתק את ספוטיפיי? הווידג'ט יפסיק להציג שירים."))return;await post("spotify-disconnect");toast("ספוטיפיי נותק");});
act("npCopy",async()=>{await navigator.clipboard.writeText($("npUrl").value);toast("הקישור הועתק ✓ – הדבק אותו ב-OBS");});
act("npCopy2",async()=>{await navigator.clipboard.writeText($("npUrl2").value);toast("הקישור הועתק ✓");});
act("npCopyRedirect",async()=>{await navigator.clipboard.writeText($("npRedirect").value);toast("הועתק ✓ – הדבק ב-Redirect URIs בספוטיפיי");});
{const sp=new URLSearchParams(location.search).get("sp");if(sp){toast(sp==="ok"?"✓ ספוטיפיי מחובר!":"ההתחברות לספוטיפיי נכשלה – בדוק שה-Redirect URI הודבק בדיוק",sp!=="ok");history.replaceState(null,"","/app"+location.hash);}}

// ── חיבור חי ──
let retry=1000;function connect(){const ws=new WebSocket((location.protocol==="https:"?"wss://":"ws://")+location.host+"/api/ws");
 ws.onmessage=e=>{retry=1000;try{const m=JSON.parse(e.data);if(m.type==="state")render(m.state);}catch{}};
 ws.onclose=e=>{if(e.code===4001){location.href="/";return;}setTimeout(connect,retry);retry=Math.min(retry*2,15000);};}
fetch("/api/state").then(r=>r.status===401?(location.href="/"):r.json()).then(s=>s&&s.settings&&render(s)).catch(()=>{});connect();
`;

export function dashboardPage() {
  return `<!doctype html><html lang="he" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Catego Studio</title><style>${BASE_STYLE}${STYLE}</style></head><body>${BODY}<script>${SCRIPT}</script></body></html>`;
}
