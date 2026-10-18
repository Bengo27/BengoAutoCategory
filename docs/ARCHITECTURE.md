# ארכיטקטורה

## זרימה

```
Win32ForegroundWindowService ─┐
FakeForegroundWindowService ──┤ (מצב בדיקה)
                              ▼
                    DetectionCoordinator      ← לולאה ברקע, כל שנייה
                              │
                              ▼
                    GameDetectionService      ← משווה מול CategoryMappingService (game_mappings.json)
                              │  DetectionResult
                              ▼
                       CategoryEngine         ← טיימר אימות, Alt-Tab, הגבלת קצב, חזרה לברירת מחדל
                              │
                              ▼
             SwitchingCategoryChangeService     ← מחובר + "שינוי אמיתי"? → קיק, אחרת → סימולציה
                     ├─ SimulatedCategoryChangeService   (רק יומן)
                     └─ PlatformCategoryChangeService
                              │
                              ▼
                     IStreamingPlatform
                       ├─ KickPlatform    ← OAuth 2.1 + PKCE, PATCH /public/v1/channels
                       └─ TwitchPlatform  (מקום שמור)

                     KickSyncService      ← פעם בדקה: GET /public/v1/channels → CategoryEngine.SyncFromPlatform
```

הממשק (WPF) **רק מאזין** לאירועים (`DetectionUpdated`, `StateChanged`, `EntryAdded`) ושולח פקודות (ביטול, שנה עכשיו, הוסף כלל). אין לוגיקת זיהוי בממשק.

## פרויקטים

| פרויקט | תפקיד | תלות ב-Windows |
|---|---|---|
| `AutoCategory.Core` | כל הלוגיקה: מודלים, שירותים, מנוע, אחסון | לא (net8.0) |
| `AutoCategory.App` | ממשק WPF, Win32, מגש מערכת, הפעלה עם Windows | כן (net8.0-windows) |
| `AutoCategory.Tests` | 45 בדיקות אוטומטיות (מנוע + קיק מול שרת מדומה) | לא |

ההפרדה מאפשרת לבדוק את כל הלוגיקה החשובה אוטומטית, בלי Windows ובלי משחקים.

## Core

```
Models/
  GameMapping        כלל: exe → קטגוריה (+ תנאי כותרת, דו-משמעי, KickCategoryId לשלב 2)
  DetectionModels    ForegroundInfo, DetectionResult, DetectionKind (None/Ignored/Unknown/Game)
  ActivityLogEntry   רשומת יומן
  AppSettings        הגדרות
Services/
  ForegroundWindowService   IForegroundWindowService + FakeForegroundWindowService
  GameDetectionService      זיהוי + ודאות (100% / 80% / 0%) + משחקים ברקע
  CategoryMappingService    CRUD לכללים ולרשימת ההתעלמות, שמירה ל-JSON, בטוח לחוטים
  CategoryEngine            מכונת המצבים (ראו למטה)
  CategoryChangeService     ICategoryChangeService + SimulatedCategoryChangeService
  DetectionCoordinator      לולאת הזיהוי
  LoggingService            500 אירועים בזיכרון + קובץ יומי
  SettingsService           settings.json
Platforms/
  StreamingPlatforms        IStreamingPlatform, TwitchPlatform, PlatformCategoryChangeService
  Kick/
    KickModels              כתובות רשמיות, מודלים
    KickAuthStore           kick_auth.json – Client Secret וטוקנים מוצפנים (ISecretProtector)
    KickApiClient           OAuth (PKCE, החלפת קוד, רענון), משתמש, ערוץ, עדכון קטגוריה, חיפוש קטגוריות
    LoopbackCallbackListener  שרת מקומי זעיר (TcpListener על localhost) שמקבל את החזרה מדף ההתחברות
    KickPlatform            התחברות/ניתוק, שינוי קטגוריה לפי שם או מזהה, מטמון מזהים
    KickSyncService         סנכרון הקטגוריה האמיתית מהערוץ
Storage/
  JsonStore                 כתיבה אטומית (קובץ זמני + החלפה), גיבוי קובץ פגום
  DefaultData               כללים ורשימת התעלמות ברירת מחדל
```

## מצבי המנוע (`EnginePhase`)

| מצב | מתי |
|---|---|
| `Idle` | הקטגוריה תואמת, או שאין מה לעשות |
| `Pending` | משחק חדש בחזית – ספירה לאחור |
| `PendingPaused` | יש שינוי ממתין אבל תוכנה מותעלמת/לא מוכרת בחזית – הספירה עוצרת |
| `AwaitingManual` | אומת, אבל "קטגוריה אוטומטית" כבויה – מחכה ל"שנה עכשיו" |
| `Cooldown` | אומת, אבל עוד לא עבר הזמן המינימלי מהשינוי הקודם |
| `ReturningToDefault` | אין משחק – ספירה לחזרה ל-Just Chatting |
| `Paused` | הזיהוי הושהה מהמגש |

### כללי בטיחות (ממומשים ונבדקים)

1. שינוי רק אחרי שהמשחק החדש היה בחזית לאורך כל זמן האימות.
2. תוכנות מותעלמות ולא מוכרות "שקופות" – לא מבטלות ולא מקדמות.
3. חזרה למשחק של הקטגוריה הנוכחית מבטלת את השינוי הממתין.
4. משחק שלישי מחליף את השינוי הממתין ומתחיל ספירה מחדש.
5. לעולם לא "משנים" לקטגוריה שכבר נבחרה.
6. הגבלת קצב בין שינויים אוטומטיים ("שנה עכשיו" ידני עוקף אותה).
7. משחק ברקע מחזיק קטגוריה אבל לעולם לא מתחיל שינוי.
8. "ביטול" ידני תקף עד שעוברים למשחק אחר.
9. פער זמן גדול (שינה של המחשב) לא נספר כזמן משחק.
10. כישלון בשינוי לא משנה את הקטגוריה הנוכחית ונרשם ביומן.

## קיק (שלב 2)

כל הכתובות לקוחות מהתיעוד הרשמי (`KickEngineering/KickDevDocs` ו-`api.kick.com/swagger/doc.yaml`):

| פעולה | בקשה | הרשאה |
|---|---|---|
| התחברות | `GET https://id.kick.com/oauth/authorize` (code + PKCE S256 + state) | — |
| החלפת קוד / רענון | `POST https://id.kick.com/oauth/token` (כולל client_secret) | — |
| ניתוק | `POST https://id.kick.com/oauth/revoke` | — |
| משתמש מחובר | `GET /public/v1/users` | `user:read` |
| ערוץ + קטגוריה נוכחית | `GET /public/v1/channels` | `channel:read` |
| שינוי קטגוריה | `PATCH /public/v1/channels` עם `{"category_id": N}` → 204 | `channel:write` |
| חיפוש קטגוריות | `GET /public/v1/categories?q=` (חיפוש חופשי), `GET /public/v2/categories?name=` | — |

**זרימת שינוי:** לכלל יש `KickCategoryId`? → PATCH ישיר. אין? → חיפוש בשם המדויק (v2, ואז v1), שמירת המזהה בכלל, ואז PATCH.

**טוקנים:** רענון אוטומטי דקה לפני תפוגה, ופעם נוספת אחרי 401. כל רענון מחליף גם את ה-refresh token, ולכן הרענון מסונכרן (רק אחד בכל פעם). רענון שנכשל → ניתוק והודעה "התחבר מחדש".

**סנכרון:** אם הקטגוריה בערוץ שונה ממה שהתוכנה קבעה, זה נחשב שינוי ידני של הסטרימר: הקטגוריה הנוכחית מתעדכנת, והמשחק שבחזית לא יוצע שוב עד שיחליף משחק.

**ה-Client Secret:** קיק דורש אותו בהחלפת קוד וברענון. כרגע כל סטרימר יוצר אפליקציה משלו. להפצה רחבה: שרת קטן שמחזיק את ה-Secret ומבצע רק את שתי הקריאות ל-`/oauth/token` – מחליפים את `KickApiClient.PostTokenAsync` בקריאה לשרת.
