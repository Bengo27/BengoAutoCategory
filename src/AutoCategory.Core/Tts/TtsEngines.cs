using System.Net;
using System.Text;
using System.Text.Json;
using AutoCategory.Core.Models;
using AutoCategory.Core.Services;

namespace AutoCategory.Core.Tts;

/// <summary>
/// הקולות האיכותיים (Hila / Avri) דרך שרת ההתחברות של Catego (Cloudflare), שמעביר ל-Azure Speech.
/// המפתח של Azure נמצא רק בשרת.
/// </summary>
public sealed class CloudTtsEngine : ITtsEngine
{
    private readonly HttpClient _http;
    private readonly Func<string?> _serverUrl;

    public CloudTtsEngine(HttpClient http, Func<string?> serverUrl)
    {
        _http = http;
        _serverUrl = serverUrl;
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_serverUrl());

    public async Task<TtsAudio> SynthesizeAsync(TtsRequest request, CancellationToken ct)
    {
        var url = _serverUrl();
        if (string.IsNullOrWhiteSpace(url)) throw new TtsException("השרת של הקולות האיכותיים לא מוגדר בגרסה הזו.");

        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["text"] = request.Text,
            ["voice"] = request.Voice,
            ["rate"] = request.RatePercent,
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, url.TrimEnd('/') + "/tts")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new TtsException("אין חיבור לשרת הקולות.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new TtsException("שרת הקולות לא ענה בזמן.", ex);
        }

        using (res)
        {
            if (!res.IsSuccessStatusCode)
            {
                throw new TtsException(res.StatusCode switch
                {
                    HttpStatusCode.ServiceUnavailable => "הקולות האיכותיים עוד לא הופעלו בשרת.",
                    HttpStatusCode.TooManyRequests => "המכסה החודשית של הקולות האיכותיים נגמרה.",
                    _ => $"שרת הקולות החזיר שגיאה {(int)res.StatusCode}.",
                });
            }
            var data = await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (data.Length < 100) throw new TtsException("שרת הקולות החזיר קובץ שמע ריק.");
            return new TtsAudio(data, res.Content.Headers.ContentType?.MediaType ?? "audio/mpeg");
        }
    }
}

/// <summary>
/// בוחר מנוע לפי הקול: Hila/Avri → השרת; Asaf → Windows.
/// אם השרת נכשל – עובר לקול של Windows כדי שההקראה לא תיעצר.
/// </summary>
public sealed class FallbackTtsEngine : ITtsEngine
{
    private readonly ITtsEngine _cloud;
    private readonly ITtsEngine? _local;
    private readonly ILoggingService _log;
    private DateTime _lastWarnUtc = DateTime.MinValue;

    public FallbackTtsEngine(ITtsEngine cloud, ITtsEngine? local, ILoggingService log)
    {
        _cloud = cloud;
        _local = local;
        _log = log;
    }

    public async Task<TtsAudio> SynthesizeAsync(TtsRequest request, CancellationToken ct)
    {
        if (!TtsVoices.Get(request.Voice).IsCloud)
            return await (_local ?? throw new TtsException("הקול של Windows לא זמין במחשב הזה."))
                .SynthesizeAsync(request, ct).ConfigureAwait(false);

        try
        {
            return await _cloud.SynthesizeAsync(request, ct).ConfigureAwait(false);
        }
        catch (TtsException ex) when (_local is not null)
        {
            // לא להציף את היומן – אזהרה אחת לכמה דקות
            if (DateTime.UtcNow - _lastWarnUtc > TimeSpan.FromMinutes(5))
            {
                _lastWarnUtc = DateTime.UtcNow;
                _log.Log(LogLevel.Warning, "הקול האיכותי לא זמין – מקריא בקול של Windows", ex.Message);
            }
            return await _local.SynthesizeAsync(request with { Voice = TtsVoices.Windows }, ct).ConfigureAwait(false);
        }
    }
}
