using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EmergencyPlatform.Models;

namespace EmergencyPlatform.Services;

/// <summary>
/// Thin wrapper around Azure OpenAI REST API.
/// Configured via appsettings: AzureOpenAI:Endpoint, ApiKey, DeploymentName
/// </summary>
public class AzureOpenAIService
{
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _deployment;
    private readonly ILogger<AzureOpenAIService> _logger;

    public AzureOpenAIService(IConfiguration config, IHttpClientFactory factory,
        ILogger<AzureOpenAIService> logger)
    {
        _http       = factory.CreateClient("AzureOpenAI");
        _endpoint   = config["AzureOpenAI:Endpoint"]!;
        _apiKey     = config["AzureOpenAI:ApiKey"]!;
        _deployment = config["AzureOpenAI:DeploymentName"] ?? "gpt-4o";
        _logger     = logger;
    }

    // ── Text-only completion ─────────────────────────────────────────────────

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage,
        int maxTokens = 1000)
    {
        var body = new
        {
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userMessage  }
            },
            max_tokens  = maxTokens,
            temperature = 0.2
        };

        return await PostAsync(body);
    }

    // ── Vision completion (GPT-4o with image) ────────────────────────────────

    public async Task<string> CompleteWithImageAsync(string systemPrompt, string userText,
        string base64Image, string mimeType = "image/jpeg", int maxTokens = 1000)
    {
        var body = new
        {
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new
                {
                    role    = "user",
                    content = new object[]
                    {
                        new { type = "text", text = userText },
                        new
                        {
                            type       = "image_url",
                            image_url  = new { url = $"data:{mimeType};base64,{base64Image}" }
                        }
                    }
                }
            },
            max_tokens  = maxTokens,
            temperature = 0.2
        };

        return await PostAsync(body);
    }

    // ── Internal HTTP helper ─────────────────────────────────────────────────

    private async Task<string> PostAsync(object body)
    {
        var url = $"{_endpoint}/openai/deployments/{_deployment}/chat/completions?api-version=2024-02-01";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("api-key", _apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(body),
            Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    // ── Utility: extract JSON block from LLM response ─────────────────────────

    public static string ExtractJson(string llmResponse)
    {
        var start = llmResponse.IndexOf('{');
        var end   = llmResponse.LastIndexOf('}');
        return start >= 0 && end > start
            ? llmResponse[start..(end + 1)]
            : llmResponse;
    }
}
