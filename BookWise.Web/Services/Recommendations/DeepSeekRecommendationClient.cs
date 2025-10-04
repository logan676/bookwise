using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BookWise.Web.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookWise.Web.Services.Recommendations;

public sealed class DeepSeekRecommendationClient : IDeepSeekRecommendationClient
{
    private readonly HttpClient _httpClient;
    private readonly DeepSeekOptions _options;
    private readonly ILogger<DeepSeekRecommendationClient> _logger;
    private readonly JsonSerializerOptions _serializerOptions;

    public DeepSeekRecommendationClient(
        HttpClient httpClient,
        IOptions<DeepSeekOptions> options,
        ILogger<DeepSeekRecommendationClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        if (!_httpClient.DefaultRequestHeaders.Contains("Authorization"))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    public async Task<IReadOnlyList<AuthorSuggestion>> GetRecommendedAuthorsAsync(
        string focusAuthor,
        IReadOnlyCollection<string> libraryAuthors,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(focusAuthor) || libraryAuthors.Count == 0)
        {
            return Array.Empty<AuthorSuggestion>();
        }

        if (string.IsNullOrWhiteSpace(_options.Endpoint) || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogDebug("DeepSeek client skipped because configuration is incomplete.");
            return Array.Empty<AuthorSuggestion>();
        }

        if (!Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var endpoint))
        {
            _logger.LogWarning("DeepSeek endpoint '{Endpoint}' is invalid.", _options.Endpoint);
            return Array.Empty<AuthorSuggestion>();
        }

        var authorContext = libraryAuthors
            .Take(Math.Max(1, _options.MaxAuthorContextCount))
            .ToArray();

        var prompt = BuildPrompt(focusAuthor, authorContext);
        var request = new ChatCompletionRequest(
            _options.Model,
            prompt);

        using var content = new StringContent(
            JsonSerializer.Serialize(request, _serializerOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "DeepSeek recommendation request failed with status {StatusCode}. Response: {Body}",
                (int)response.StatusCode,
                body);
            response.EnsureSuccessStatusCode();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var completion = await JsonSerializer.DeserializeAsync<ChatCompletionResponse>(stream, _serializerOptions, cancellationToken);

        if (completion?.Choices is null || completion.Choices.Count == 0)
        {
            return Array.Empty<AuthorSuggestion>();
        }

        foreach (var choice in completion.Choices)
        {
            if (string.IsNullOrWhiteSpace(choice.Message?.Content))
            {
                continue;
            }

            var suggestions = TryParseSuggestions(choice.Message.Content);
            if (suggestions.Count > 0)
            {
                return suggestions;
            }
        }

        return Array.Empty<AuthorSuggestion>();
    }

    private string BuildPrompt(string focusAuthor, IReadOnlyList<string> authorContext)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are an expert literary recommendation engine.");
        builder.AppendLine("Given the following list of authors from the user's library, recommend additional authors they may enjoy.");
        builder.Append("Prioritize recommendations relevant to readers who like ")
            .Append(focusAuthor)
            .AppendLine(" but avoid suggesting the same author.");
        builder.AppendLine();
        builder.AppendLine("Known authors:");
        foreach (var author in authorContext)
        {
            builder.Append("- ").AppendLine(author);
        }

        builder.AppendLine();
        builder.AppendLine("Return a strict JSON array where each item has the following shape:");
        builder.AppendLine("{");
        builder.AppendLine("  \"name\": string, ");
        builder.AppendLine("  \"rationale\": string (short sentence),");
        builder.AppendLine("  \"imageUrl\": string | null,");
        builder.AppendLine("  \"confidence\": number between 0 and 1");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.Append("Limit the response to ")
            .Append(_options.RecommendationCount)
            .AppendLine(" items. Do not include any text outside of the JSON array.");

        return builder.ToString();
    }

    private IReadOnlyList<AuthorSuggestion> TryParseSuggestions(string completionContent)
    {
        try
        {
            using var document = JsonDocument.Parse(completionContent);
            return ParseFromJsonDocument(document);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse DeepSeek response. Content: {Content}", completionContent);
            return Array.Empty<AuthorSuggestion>();
        }
    }

    private IReadOnlyList<AuthorSuggestion> ParseFromJsonDocument(JsonDocument document)
    {
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return ParseSuggestionArray(document.RootElement);
        }

        if (document.RootElement.TryGetProperty("recommendations", out var recommendationsElement) && recommendationsElement.ValueKind == JsonValueKind.Array)
        {
            return ParseSuggestionArray(recommendationsElement);
        }

        return Array.Empty<AuthorSuggestion>();
    }

    private IReadOnlyList<AuthorSuggestion> ParseSuggestionArray(JsonElement arrayElement)
    {
        var results = new List<AuthorSuggestion>();
        foreach (var element in arrayElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = element.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string? rationale = null;
            if (element.TryGetProperty("rationale", out var rationaleElement) && rationaleElement.ValueKind == JsonValueKind.String)
            {
                rationale = rationaleElement.GetString();
            }

            string? imageUrl = null;
            if (element.TryGetProperty("imageUrl", out var imageElement) && imageElement.ValueKind == JsonValueKind.String)
            {
                imageUrl = imageElement.GetString();
            }

            decimal? confidence = null;
            if (element.TryGetProperty("confidence", out var confidenceElement) && confidenceElement.ValueKind is JsonValueKind.Number)
            {
                if (confidenceElement.TryGetDecimal(out var value))
                {
                    confidence = value;
                }
            }

            results.Add(new AuthorSuggestion(name!, rationale, imageUrl, confidence));
        }

        return results;
    }

    private sealed class ChatCompletionRequest
    {
        public ChatCompletionRequest(string model, string prompt)
        {
            Model = model;
            Messages =
            [
                new ChatMessage("system", "You respond with structured JSON only."),
                new ChatMessage("user", prompt)
            ];
        }

        [JsonPropertyName("model")]
        public string Model { get; }

        [JsonPropertyName("messages")]
        public IReadOnlyList<ChatMessage> Messages { get; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; init; } = 800;

        [JsonPropertyName("temperature")]
        public decimal Temperature { get; init; } = 0.3m;

        [JsonPropertyName("response_format")]
        public ResponseFormatPayload ResponseFormat { get; init; } = new("json_object");

        public sealed record ChatMessage(string Role, string Content)
        {
            [JsonPropertyName("role")]
            public string Role { get; } = Role;

            [JsonPropertyName("content")]
            public string Content { get; } = Content;
        }

        public sealed record ResponseFormatPayload(string Type)
        {
            [JsonPropertyName("type")]
            public string Type { get; } = Type;
        }
    }

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<CompletionChoice> Choices { get; set; } = new();

        public sealed class CompletionChoice
        {
            [JsonPropertyName("message")]
            public ChatMessage? Message { get; set; }
        }

        public sealed class ChatMessage
        {
            [JsonPropertyName("role")]
            public string? Role { get; set; }

            [JsonPropertyName("content")]
            public string? Content { get; set; }
        }
    }
}
