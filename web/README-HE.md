# Catego TTS – האתר

אתר הקראה לסטרימרים: צופים קונים פרס בנקודות הערוץ בקיק, וההודעה מוקראת בשידור דרך OBS.

- כתובת: `https://catego.<subdomain>.workers.dev`
- רץ על Cloudflare Workers + Durable Objects (אובייקט אחד לכל סטרימר).
- קולות: Azure Speech (Hila / Avri).

## משתנים ב-Cloudflare (Settings → Variables and Secrets)
| שם | סוג | מה |
|---|---|---|
| `KICK_CLIENT_ID` | Text | ה-Client ID של אפליקציית הקיק |
| `KICK_CLIENT_SECRET` | Secret | ה-Client Secret (משמש גם לחתימת ההתחברויות ולהצפנת הטוקנים) |
| `AZURE_SPEECH_KEY` | Secret | המפתח של משאב Speech ב-Azure |
| `AZURE_SPEECH_REGION` | Text | למשל `westeurope` |
| `GEMINI_API_KEY` | Secret | מפתח מ-aistudio.google.com (מסלול חינמי) – למצב "מהשרת (Gemini)" |

## באפליקציה בקיק
- Redirect URL: `https://catego.<subdomain>.workers.dev/auth/callback`
- Scopes: `user:read channel:read channel:rewards:read channel:rewards:write events:subscribe`
- Webhooks: מופעל, URL: `https://catego.<subdomain>.workers.dev/kick/webhook`

## בדיקות
`node web/test/test.mjs`
