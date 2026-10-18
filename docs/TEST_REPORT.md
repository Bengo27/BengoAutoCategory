# דוח בדיקות – שלבים 1 ו-2

תאריך: 18.10.2026

## חלק א' – בדיקות אוטומטיות: 45/45 עברו ✅

הורצו על ‎.NET 8 (`dotnet run --project tests\AutoCategory.Tests`). הבדיקות משתמשות בשעון מדומה, כך ש"20 שניות" נבדקות בדיוק ובלי המתנה. `build.bat` מריץ אותן לפני כל בנייה.

| דרישה מהמסמך | בדיקות | תוצאה |
|---|---|---|
| זיהוי משחק מוכר (100%) | T01 | ✅ |
| קובץ דו-משמעי (80%), javaw.exe רק עם "Minecraft" בכותרת | T02 | ✅ |
| רשימת התעלמות (OBS, Discord, Explorer, Spotify, Steam, דפדפנים) | T03, T15 | ✅ |
| תוכנה לא מוכרת (0%) | T04, T27 | ✅ |
| יצירת כלל ידני + שמירה אחרי הפעלה מחדש | T05 | ✅ |
| עריכה/מחיקה של כלל + שמירה | T06 | ✅ |
| הוספה/הסרה מרשימת ההתעלמות + שמירה | T07, T08 | ✅ |
| שמירת הגדרות + תיקון ערכים לא חוקיים | T09 | ✅ |
| קבצים פגומים → ברירת מחדל + גיבוי | T10 | ✅ |
| טיימר אימות 20 שניות (לא 19) | T11 | ✅ |
| Alt-Tab לתוכנה מותעלמת – הספירה מושהית, לא מתאפסת | T12 | ✅ |
| Alt-Tab קצר למשחק אחר וחזרה – ביטול | T13 | ✅ |
| משחק שלישי מחליף שינוי ממתין | T14 | ✅ |
| אף פעם לא משנים בגלל OBS/Discord/Steam/Spotify/Explorer/דפדפן | T15 | ✅ |
| אין שינוי חוזר לאותה קטגוריה | T16 | ✅ |
| הגבלת קצב בין שינויים | T17 | ✅ |
| מצב אוטומטי כבוי → הצעה בלבד; "שנה עכשיו" | T18, T19 | ✅ |
| "ביטול" ידני לא חוזר עד החלפת משחק | T20 | ✅ |
| חזרה ל-Just Chatting אחרי 5 דקות בלי משחק | T21 | ✅ |
| משחק ברקע מחזיק קטגוריה ולא מתחיל שינוי | T22 | ✅ |
| השהיית זיהוי מבטלת שינוי ממתין | T23 | ✅ |
| שינה של המחשב לא נספרת כזמן משחק | T24 | ✅ |
| כישלון בשינוי לא משנה קטגוריה | T25 | ✅ |
| הפעלה מחדש – הקטגוריה הנוכחית נשמרת, אין שינוי כפול | T26 | ✅ |
| מצב בדיקה מקצה לקצה + רישום "SIMULATED CATEGORY CHANGE" | T27 | ✅ |
| החלון של התוכנה עצמה: התצוגה נשמרת, הספירה מושהית | T29 | ✅ |

## חלק א2 – חיבור לקיק מול שרת מדומה (16 בדיקות) ✅

השרת המדומה עונה בדיוק לפי התיעוד הרשמי של קיק (כתובות, שדות, קודי תשובה), ובודק שהתוכנה שולחת את מה שקיק מצפה לו.

| מה נבדק | בדיקה | תוצאה |
|---|---|---|
| PKCE S256 + כל פרמטרי כתובת ההתחברות | K01 | ✅ |
| החלפת קוד בטוקן + שמירה מוצפנת (אין סוד בטקסט גלוי בקובץ) | K02 | ✅ |
| שינוי קטגוריה: PATCH /public/v1/channels עם category_id + Bearer | K03 | ✅ |
| שינוי לפי שם: חיפוש קטגוריה, שמירת המזהה, מטמון | K04 | ✅ |
| קטגוריה שלא קיימת → הודעה ברורה, בלי PATCH | K05 | ✅ |
| 401 → רענון טוקן → ניסיון חוזר מוצלח, refresh token מוחלף | K06 | ✅ |
| רענון מראש כשהטוקן פג | K07 | ✅ |
| רענון נכשל → ניתוק + בקשה להתחבר מחדש | K08 | ✅ |
| השרת המקומי מקבל את החזרה מהדפדפן | K09 | ✅ |
| התחברות מלאה מקצה לקצה (דפדפן מדומה) | K10 | ✅ |
| הגנה מ-state מזויף | K11 | ✅ |
| המשתמש סירב בדף של קיק | K12 | ✅ |
| מקצה לקצה: GTA בחזית 20 שניות → הקטגוריה בקיק משתנה + המזהה נשמר בכלל | K13 | ✅ |
| שינוי ידני באתר של קיק → התוכנה מתעדכנת ולא "נלחמת" בסטרימר | K14 | ✅ |
| מתג סימולציה/אמיתי | K15 | ✅ |
| ניתוק: ביטול בשרת + מחיקת טוקנים | K16 | ✅ |

## חלק ג' – חיבור לקיק האמיתי (18.10.2026)

| בדיקה | תוצאה |
|---|---|
| `build.bat` עובר עם מסכי הקיק | ✅ |
| התחברות בדפדפן → "מחובר לקיק בתור …" | ✅ |
| "בדוק ותקן מול קיק" | ✅ (13 נמצאו, GTA/Overwatch תוקנו בהתאמה חכמה) |
| "חפש בקיק" בחלון הכלל | ⏳ |
| מצב בדיקה GTA5.exe 20 שניות → שינוי אמיתי בקיק (בלי שידור) | ✅ |
| שינוי ידני באתר → מתעדכן בתוכנה תוך דקה | ⏳ |
| הפעלה מחדש של התוכנה → נשאר מחובר | ⏳ |

## חלק ב' – בדיקות על Windows (שלב 1): עברו ✅

נבדקו על מחשב Windows אמיתי (18.10.2026):

| בדיקה | איך בודקים | תוצאה |
|---|---|---|
| הבנייה עוברת | `build.bat` | ✅ (אחרי תיקון using System.IO) |
| זיהוי חלון אמיתי | לפתוח Notepad / Discord ולראות בלוח הבקרה תהליך, קובץ, כותרת ושעה | ✅ |
| זיהוי משחק אמיתי | לפתוח משחק מותקן, לחכות 20 שניות, לראות שינוי מדומה ביומן | ✅ |
| Alt-Tab אמיתי | באמצע הספירה Alt-Tab לדיסקורד – הספירה נעצרת | ✅ |
| "הוסף כלל" מהממשק | תוכנה לא מוכרת → הוסף כלל → מופיע בכללי קטגוריות | ✅ |
| שמירה אחרי הפעלה מחדש | לצאת מהמגש ולהפעיל שוב – הכלל וההגדרות נשמרו | ✅ |
| מגש מערכת | סגירת החלון → ממשיך ברקע; תפריט: הפעל/כבה, פתח, השהה, יציאה | ✅ |
| הפעלה עם Windows | להפעיל בהגדרות, לבדוק במנהל המשימות → אתחול | ✅ |
| התראות | שינוי מדומה מציג התראה ליד השעון | ✅ |
| עברית ו-RTL | כל המסכים מימין לשמאל וקריאים | ✅ |

## פלט ההרצה האחרונה

```
PASS  T01_KnownGame_ExactMatch_100
PASS  T02_AmbiguousExecutable_80_OnlyWithTitle
PASS  T03_IgnoreList_Defaults
PASS  T04_UnknownApplication_0
PASS  T05_ManualMapping_PersistsAfterRestart
PASS  T06_EditAndDeleteMapping_Persist
PASS  T07_IgnoreList_AddRemove_Persist
PASS  T08_MappingBeatsIgnore
PASS  T09_Settings_PersistAndSanitize
PASS  T10_CorruptFiles_FallBackToDefaults
PASS  T11_Timer_ChangesOnlyAfter20Seconds
PASS  T12_AltTabToIgnoredApp_PausesTimer_DoesNotReset
PASS  T13_BriefAltTabToOtherGame_ReturnCancels
PASS  T14_DifferentGame_ReplacesPending
PASS  T15_OnlyIgnoredApps_NeverChange
PASS  T16_NoRepeatChangeToSameCategory
PASS  T17_Cooldown_BetweenAutomaticChanges
PASS  T18_AutoOff_SuggestsOnly_ChangeNowWorks
PASS  T19_ChangeNow_SkipsTimer
PASS  T20_Cancel_NotReSuggestedUntilGameChanges
PASS  T21_ReturnToDefault_AfterNoGame
PASS  T22_BackgroundGame_HoldsCategory_NeverStartsChange
PASS  T23_Pause_CancelsPending
PASS  T24_SleepGap_IsClamped
PASS  T25_FailedChange_KeepsCategory
PASS  T26_Restart_RestoresCurrentCategory
PASS  T27_Coordinator_TestMode_EndToEnd
PASS  T29_OwnWindow_KeepsDisplay_AndPausesTimer
PASS  T28_ExportDefaultMappingsFile
PASS  K01_Pkce_And_AuthorizeUrl
PASS  K02_ExchangeCode_StoresEncryptedTokens
PASS  K03_SetCategory_SendsPatchWithId
PASS  K04_SetCategory_ByName_ResolvesAndCaches
PASS  K05_UnknownCategory_FailsClearly
PASS  K06_Expired401_RefreshesAndRetries
PASS  K07_ProactiveRefresh_WhenExpired
PASS  K08_RefreshFails_Disconnects
PASS  K09_LoopbackListener_ReceivesCallback
PASS  K10_ConnectAsync_FullFlow
PASS  K11_ConnectAsync_RejectsWrongState
PASS  K12_ConnectAsync_UserDenied
PASS  K13_EndToEnd_GameDetected_ChangesKickCategory
PASS  K14_Sync_RespectsManualChangeOnKick
PASS  K15_SwitchingService_SimulationWhenNotLive
PASS  K16_Disconnect_ClearsTokens
Total: 45, Passed: 45, Failed: 0
```
