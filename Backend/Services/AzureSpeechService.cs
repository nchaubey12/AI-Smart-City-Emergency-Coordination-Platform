using System.Net.Http.Headers;
using System.Text.Json;

namespace EmergencyPlatform.Services;

/// <summary>
/// Azure AI Speech – converts audio bytes to text via REST batch transcription.
/// For real-time streaming, replace with the Speech SDK.
/// Config keys: AzureSpeech:Endpoint, AzureSpeech:ApiKey, AzureSpeech:Region
/// </summary>
public class AzureSpeechService
{
    private readonly HttpClient _http;
    private readonly string _key;
    private readonly string _region;
    private readonly ILogger<AzureSpeechService> _logger;

    public AzureSpeechService(IConfiguration config, IHttpClientFactory factory,
        ILogger<AzureSpeechService> logger)
    {
        _http   = factory.CreateClient();
        _key    = config["AzureSpeech:ApiKey"]!;
        _region = config["AzureSpeech:Region"] ?? "eastus";
        _logger = logger;
    }

    /// <summary>
    /// Transcribes raw audio bytes (WAV/MP3/OGG) to plain text.
    /// Uses the simple "recognize" REST endpoint (single-turn, &lt;60 s audio).
    /// </summary>
    public async Task<string> TranscribeAsync(byte[] audioBytes, string mimeType = "audio/wav")
    {
        var url = $"https://{_region}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1"
                + "?language=en-US&format=simple";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("Ocp-Apim-Subscription-Key", _key);
        req.Content = new ByteArrayContent(audioBytes);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

        var resp = await _http.SendAsync(req);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Speech API returned {Status}", resp.StatusCode);
            return "[Transcription unavailable]";
        }

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("DisplayText", out var text))
            return text.GetString() ?? string.Empty;

        return string.Empty;
    }
}
