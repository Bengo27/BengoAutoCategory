# הפצה לסטרימרים – הגדרה חד-פעמית

שלושה חלקים: **שרת התחברות** (Cloudflare), **מאגר ועדכונים** (GitHub), ו**קובץ הגדרות ההפצה**.
אחרי ההגדרה, כל גרסה חדשה מתפרסמת בלחיצה, והסטרימרים מתעדכנים לבד.

---

## חלק 1: שרת ההתחברות ב-Cloudflare (כ-10 דקות)

השרת מחזיק את ה-Client Secret של אפליקציית הקיק שלך, כך שסטרימרים לא צריכים אפליקציה משלהם.
הוא לא שומר כלום ולא רואה סיסמאות. הוא רק מעביר לקיק את בקשת ההתחברות עם ה-Secret.

1. נרשמים בחינם ב-https://dash.cloudflare.com/sign-up (לא צריך כרטיס אשראי ולא דומיין).
2. בתפריט: **Compute (Workers)** → **Workers & Pages** → **Create** → **Create Worker** (Start with Hello World).
3. שם: `bengo-kick-broker` → **Deploy**.
4. **Edit code** → מוחקים את כל הקוד → מדביקים את כל התוכן של `server/worker.js` → **Deploy**.
5. חוזרים לעמוד ה-Worker → **Settings** → **Variables and Secrets** → **Add**:
   - `KICK_CLIENT_ID`: סוג **Text**, הערך הוא ה-Client ID של אפליקציית הקיק שלך.
   - `KICK_CLIENT_SECRET`: סוג **Secret**, הערך הוא ה-Client Secret.
   - **Deploy**.
6. בודקים: פותחים בדפדפן `https://bengo-kick-broker.<השם-שלך>.workers.dev/health`.
   צריך להופיע `"ok":true` ואת ה-client_id שלך.

> ה-Secret נשמר רק ב-Cloudflare. הוא לא נמצא בקוד, לא ב-GitHub ולא בתוכנה.
> הגבול החינמי הוא 100,000 בקשות ביום. כל סטרימר משתמש בכמה בקשות ביום בלבד.

---

## חלק 2: GitHub (כ-10 דקות)

1. נרשמים ב-https://github.com/signup.
1א. בתיקיית הפרויקט: לחיצה כפולה על **`install-github-workflows.bat`**. זה מעתיק לתיקייה `.github\workflows` את קבצי הבנייה האוטומטית.
2. מתקינים **GitHub Desktop**: https://desktop.github.com
3. ב-GitHub Desktop: **File → Add local repository** → בוחרים את `D:\פרויקט קלוד\BengoAutoCategory`.
   מופיעה ההודעה "This directory does not appear to be a Git repository" → **create a repository** → **Create repository**.
4. **Publish repository**:
   - Name: `BengoAutoCategory`
   - **מורידים את הסימון מ-"Keep this code private"**. העדכון האוטומטי קורא את הגרסאות מ-GitHub בלי סיסמה, וזה עובד רק במאגר ציבורי. אין סודות בקוד: ה-Client ID ציבורי ממילא, וה-Secret נמצא רק ב-Cloudflare.
5. אחרי הפרסום, ב-GitHub באתר: **Actions** → אם מופיעה בקשה, מאשרים הפעלה של Workflows.

---

## חלק 3: קובץ הגדרות ההפצה

הקובץ `src/AutoCategory.App/app_config.json` מוטמע בתוך ה-exe:

```json
{
  "kickClientId": "ה-Client ID של אפליקציית הקיק (ציבורי)",
  "kickBrokerUrl": "https://bengo-kick-broker.<השם-שלך>.workers.dev",
  "gitHubRepo": "<שם-המשתמש-בגיטהאב>/BengoAutoCategory"
}
```

אחרי העריכה: ב-GitHub Desktop → **Commit to main** → **Push origin**.

---

## פרסום גרסה חדשה (בכל פעם)

1. ב-GitHub Desktop: **Commit to main** → **Push origin** (אם היו שינויים).
2. באתר GitHub: **Actions** → **Release** → **Run workflow**
   - **version**: מספר גבוה מהקודם, למשל `0.3.0`, אחר כך `0.3.1` וכו'.
   - **notes**: מה חדש (בעברית).
3. אחרי כ-5 דקות הגרסה מופיעה ב-**Releases**, וכל התוכנות הפתוחות יציגו "גרסה חדשה זמינה" (תוך 6 שעות, או מיד ב"בדוק עדכונים").

**קישור קבוע להורדה לסטרימרים:**
`https://github.com/<שם-המשתמש>/BengoAutoCategory/releases/latest/download/Catego.exe`

---

## מה הסטרימרים צריכים לעשות

ראו [STREAMER_GUIDE.md](STREAMER_GUIDE.md): להוריד, להריץ, וללחוץ "התחבר לקיק". זהו.
