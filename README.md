# Bengo AutoCategory

תוכנת Windows לסטרימרים שמזהה איזה משחק פתוח בחזית ומציעה (או מבצעת) שינוי קטגוריה בסטרים.

**שלב 2: חיבור לקיק.** אחרי התחברות (עמוד "חיבור לקיק" בתוכנה), הקטגוריה משתנה בערוץ באמת דרך ה-API הרשמי של Kick. בלי חיבור – הכול עובד במצב סימולציה כמו בשלב 1. מדריך הגדרה: [docs/KICK_SETUP.md](docs/KICK_SETUP.md).

## הרצה מהירה

1. לחיצה כפולה על `build.bat`.
   - אם ‎.NET 8 SDK לא מותקן, הסקריפט ינסה להתקין אותו אוטומטית (winget). אחרי ההתקנה – לסגור את החלון ולהריץ שוב.
2. הסקריפט מריץ את הבדיקות האוטומטיות ואז בונה את `publish\BengoAutoCategory.exe`.
3. מריצים את `BengoAutoCategory.exe`. לא נדרשות הרשאות מנהל.

קובץ ה-exe עצמאי (self-contained) – אפשר להעביר את תיקיית `publish` למחשב אחר בלי להתקין ‎.NET.

## בנייה ידנית

```
dotnet run --project tests\AutoCategory.Tests -c Release
dotnet publish src\AutoCategory.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

או לפתוח את `AutoCategory.sln` ב-Visual Studio 2022 (או Rider) ולהריץ את הפרויקט `AutoCategory.App`.

## איפה נשמרים הנתונים

`%AppData%\BengoAutoCategory\`

| קובץ | תוכן |
|---|---|
| `game_mappings.json` | כללי המשחקים + רשימת ההתעלמות |
| `settings.json` | ההגדרות + הקטגוריה הנוכחית האחרונה |
| `kick_auth.json` | פרטי החיבור לקיק (Client Secret וטוקנים – מוצפנים ב-DPAPI) |
| `logs\activity-YYYY-MM-DD.log` | יומן פעילות יומי |

בהפעלה הראשונה הקבצים מועתקים מתיקיית `Data` שליד ה-exe. אפשר לערוך את קובץ ה-JSON ידנית (כשהתוכנה סגורה); קובץ פגום מגובה אוטומטית ומוחלף בברירת מחדל.

## איך זה עובד (בקצרה)

- כל 2 שניות התוכנה בודקת איזה חלון בחזית (Windows API בלבד – בלי צילומי מסך ובלי לגעת בזיכרון של המשחק, כך שזה בטוח מול אנטי-צ'יט).
- אם הקובץ מופיע בכללים → משחק מוכר. אם הוא ברשימת ההתעלמות (OBS, Discord, דפדפנים, לאנצ'רים) → מתעלמים.
- משחק חדש צריך להיות בחזית **20 שניות** לפני שינוי. Alt-Tab לתוכנה מותעלמת **משהה** את הספירה (לא מאפס ולא מבטל). חזרה למשחק של הקטגוריה הנוכחית **מבטלת** את השינוי.
- לפחות **2 דקות** בין שינויים אוטומטיים.
- אין משחק פעיל (ברירת מחדל 5 דקות, ניתן לשינוי) → חזרה ל-Just Chatting. משחק מוכר שרץ ברקע מונע את החזרה.
- משחק חדש שמותקן בתיקיית Steam/Epic/וכו' מקבל כלל אוטומטית. משחק שאין לו קטגוריה בקיק עובר ל-**Games + Demos**.
- מחובר לקיק: כל 30 שניות התוכנה קוראת את הקטגוריה בערוץ. אם שינית קטגוריה בעצמך באתר – התוכנה מכבדת את זה ולא "נלחמת" בך עד שתחליף משחק. בתחילת שידור היא שולחת לקיק את הקטגוריה הנכונה.

## הפצה לסטרימרים

- **התחברות בלחיצה אחת:** שרת קטן ב-Cloudflare (`server/worker.js`) מחזיק את ה-Client Secret, כך שסטרימרים לא צריכים אפליקציית מפתחים משלהם.
- **עדכון אוטומטי:** התוכנה בודקת ב-GitHub Releases ומתעדכנת בלחיצה.
- **פרסום גרסה:** GitHub → Actions → Release → Run workflow.
- הגדרה: [docs/PUBLISHING.md](docs/PUBLISHING.md) · מדריך לסטרימרים: [docs/STREAMER_GUIDE.md](docs/STREAMER_GUIDE.md)

## מסמכים נוספים

- [docs/KICK_SETUP.md](docs/KICK_SETUP.md) – חיבור לקיק צעד אחרי צעד
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) – מבנה הקוד
- [docs/TEST_REPORT.md](docs/TEST_REPORT.md) – דוח בדיקות
- [docs/KNOWN_LIMITATIONS.md](docs/KNOWN_LIMITATIONS.md) – מגבלות ידועות
