using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using AutoCategory.Core.Tts;

namespace AutoCategory.App.Platform;

/// <summary>
/// הקול המובנה של Windows (Asaf) דרך SAPI – בלי אינטרנט ובלי חבילות.
/// מחפש קול עברי גם ברשימה הרגילה וגם בקולות ה-OneCore (שם Asaf בדרך כלל מותקן).
/// </summary>
public sealed class SapiTtsEngine : ITtsEngine
{
    private const string OneCoreVoices = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices";
    private const int SaftKhz22Bit16Mono = 22;
    private const int SsfmCreateForWrite = 3;
    private const int SvsfIsNotXml = 16;

    public Task<TtsAudio> SynthesizeAsync(TtsRequest request, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<TtsAudio>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.TrySetResult(Synthesize(request)); }
            catch (TtsException ex) { tcs.TrySetException(ex); }
            catch (Exception ex) { tcs.TrySetException(new TtsException("הקול של Windows נכשל: " + ex.Message, ex)); }
        })
        { IsBackground = true, Name = "Catego SAPI" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task.WaitAsync(ct);
    }

    private static TtsAudio Synthesize(TtsRequest r)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Catego");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"sapi-{Guid.NewGuid():N}.wav");

        var voice = Create("SAPI.SpVoice");
        var token = FindHebrewVoice(voice) ?? throw new TtsException(
            "לא נמצא קול עברי של Windows. הגדרות Windows ← זמן ושפה ← דיבור ← הוסף קולות ← עברית.");
        PutRef(voice, "Voice", token);
        Set(voice, "Rate", Math.Clamp(r.RatePercent / 10, -10, 10));

        var stream = Create("SAPI.SpFileStream");
        var format = Get(stream, "Format") ?? throw new TtsException("רכיב הדיבור של Windows לא תקין.");
        Set(format, "Type", SaftKhz22Bit16Mono);
        PutRef(stream, "Format", format);
        Call(stream, "Open", file, SsfmCreateForWrite, false);
        try
        {
            PutRef(voice, "AudioOutputStream", stream);
            Call(voice, "Speak", r.Text, SvsfIsNotXml);
        }
        finally
        {
            Call(stream, "Close");
        }

        try
        {
            var bytes = File.ReadAllBytes(file);
            if (bytes.Length < 100) throw new TtsException("הקול של Windows לא יצר שמע.");
            return new TtsAudio(bytes, "audio/wav");
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    private static object? FindHebrewVoice(object voice)
    {
        foreach (var useOneCore in new[] { false, true })
        {
            try
            {
                object tokens;
                if (!useOneCore)
                {
                    tokens = Call(voice, "GetVoices", "", "")!;
                }
                else
                {
                    var category = Create("SAPI.SpObjectTokenCategory");
                    Call(category, "SetId", OneCoreVoices, false);
                    tokens = Call(category, "EnumerateTokens", "", "")!;
                }
                var count = Convert.ToInt32(Get(tokens, "Count"));
                for (var i = 0; i < count; i++)
                {
                    var token = Call(tokens, "Item", i)!;
                    string lang = "", desc = "";
                    try { lang = Call(token, "GetAttribute", "Language") as string ?? ""; } catch { }
                    try { desc = Call(token, "GetDescription", 0) as string ?? ""; } catch { }
                    if (lang.Split(';').Any(l => l.Trim().Equals("40D", StringComparison.OrdinalIgnoreCase)) ||
                        desc.Contains("Hebrew", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("Asaf", StringComparison.OrdinalIgnoreCase))
                        return token;
                }
            }
            catch
            {
                // הקטגוריה לא קיימת במחשב הזה
            }
        }
        return null;
    }

    // ── COM בלי ספריות interop ──
    private static object Create(string progId) =>
        Activator.CreateInstance(Type.GetTypeFromProgID(progId) ?? throw new TtsException("רכיב הדיבור של Windows (SAPI) לא זמין."))!;

    private static object? Call(object target, string name, params object[] args) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);

    private static object? Get(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);

    private static void Set(object target, string name, object value) =>
        target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, new[] { value });

    private static void PutRef(object target, string name, object value) =>
        target.GetType().InvokeMember(name, BindingFlags.PutRefDispProperty, null, target, new[] { value });
}

/// <summary>משמיע את ההקראה ברמקולים של המחשב (OBS קולט את זה כ-Desktop Audio).</summary>
public sealed class MediaTtsPlayer : ITtsPlayer
{
    public async Task PlayAsync(TtsAudio audio, int volumePercent, CancellationToken ct)
    {
        var dispatcher = Application.Current?.Dispatcher ?? throw new TtsException("התוכנה נסגרת.");
        var dir = Path.Combine(Path.GetTempPath(), "Catego");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"tts-{Guid.NewGuid():N}{audio.Extension}");
        await File.WriteAllBytesAsync(file, audio.Data, ct).ConfigureAwait(false);

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MediaPlayer? player = null;
        try
        {
            await dispatcher.InvokeAsync(() =>
            {
                player = new MediaPlayer { Volume = Math.Clamp(volumePercent / 100.0, 0, 1) };
                player.MediaEnded += (_, _) => done.TrySetResult();
                player.MediaFailed += (_, e) =>
                    done.TrySetException(new TtsException("לא ניתן להשמיע את ההקראה: " + e.ErrorException?.Message));
                player.Open(new Uri(file));
                player.Play();
            });

            using var reg = ct.Register(() => done.TrySetCanceled());
            var finished = await Task.WhenAny(done.Task, Task.Delay(audio.EstimatedDuration + TimeSpan.FromSeconds(15)))
                .ConfigureAwait(false);
            if (finished == done.Task) await done.Task.ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await dispatcher.InvokeAsync(() =>
                {
                    player?.Stop();
                    player?.Close();
                });
            }
            catch { }
            try { File.Delete(file); } catch { }
        }
    }
}
