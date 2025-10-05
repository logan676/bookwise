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

    public async Task<IReadOnlyList<SeriesSuggestion>> GetRecommendedSeriesAsync(
        IReadOnlyCollection<string> libraryTitles,
        CancellationToken cancellationToken)
    {
        if (libraryTitles is null || libraryTitles.Count == 0)
        {
            return Array.Empty<SeriesSuggestion>();
        }

        if (string.IsNullOrWhiteSpace(_options.Endpoint) || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogDebug("DeepSeek client skipped for series because configuration is incomplete.");
            return Array.Empty<SeriesSuggestion>();
        }

        if (!Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var endpoint))
        {
            _logger.LogWarning("DeepSeek endpoint '{Endpoint}' is invalid.", _options.Endpoint);
            return Array.Empty<SeriesSuggestion>();
        }

        var context = libraryTitles
            .Take(Math.Max(1, _options.MaxAuthorContextCount))
            .ToArray();

        var prompt = BuildSeriesPrompt(context);
        var request = new ChatCompletionRequest(_options.Model, prompt);

        using var content = new StringContent(
            JsonSerializer.Serialize(request, _serializerOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "DeepSeek series request failed with status {StatusCode}. Response: {Body}",
                (int)response.StatusCode,
                body);
            response.EnsureSuccessStatusCode();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var completion = await JsonSerializer.DeserializeAsync<ChatCompletionResponse>(stream, _serializerOptions, cancellationToken);

        if (completion?.Choices is null || completion.Choices.Count == 0)
        {
            return Array.Empty<SeriesSuggestion>();
        }

        foreach (var choice in completion.Choices)
        {
            if (string.IsNullOrWhiteSpace(choice.Message?.Content))
            {
                continue;
            }

            var suggestions = TryParseSeriesSuggestions(choice.Message.Content);
            if (suggestions.Count > 0)
            {
                return suggestions;
            }
        }

        return Array.Empty<SeriesSuggestion>();
    }

    public async Task<IReadOnlyList<AdaptationSuggestion>> GetRecommendedAdaptationsAsync(
        IReadOnlyCollection<string> libraryTitles,
        CancellationToken cancellationToken)
    {
        if (libraryTitles is null || libraryTitles.Count == 0)
        {
            return Array.Empty<AdaptationSuggestion>();
        }

        if (string.IsNullOrWhiteSpace(_options.Endpoint) || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogDebug("DeepSeek client skipped for adaptations because configuration is incomplete.");
            return Array.Empty<AdaptationSuggestion>();
        }

        if (!Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var endpoint))
        {
            _logger.LogWarning("DeepSeek endpoint '{Endpoint}' is invalid.", _options.Endpoint);
            return Array.Empty<AdaptationSuggestion>();
        }

        var context = libraryTitles
            .Take(Math.Max(1, _options.MaxAuthorContextCount))
            .ToArray();

        var prompt = BuildAdaptationsPrompt(context);
        var request = new ChatCompletionRequest(_options.Model, prompt);

        using var content = new StringContent(
            JsonSerializer.Serialize(request, _serializerOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "DeepSeek adaptations request failed with status {StatusCode}. Response: {Body}",
                (int)response.StatusCode,
                body);
            response.EnsureSuccessStatusCode();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var completion = await JsonSerializer.DeserializeAsync<ChatCompletionResponse>(stream, _serializerOptions, cancellationToken);

        if (completion?.Choices is null || completion.Choices.Count == 0)
        {
            return Array.Empty<AdaptationSuggestion>();
        }

        foreach (var choice in completion.Choices)
        {
            if (string.IsNullOrWhiteSpace(choice.Message?.Content))
            {
                continue;
            }

            var suggestions = TryParseAdaptationSuggestions(choice.Message.Content);
            if (suggestions.Count > 0)
            {
                return suggestions;
            }
        }

        return Array.Empty<AdaptationSuggestion>();
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

    private IReadOnlyList<SeriesSuggestion> TryParseSeriesSuggestions(string completionContent)
    {
        try
        {
            using var document = JsonDocument.Parse(completionContent);
            return ParseSeriesFromJsonDocument(document);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse DeepSeek series response. Content: {Content}", completionContent);
            return Array.Empty<SeriesSuggestion>();
        }
    }

    private IReadOnlyList<AdaptationSuggestion> TryParseAdaptationSuggestions(string completionContent)
    {
        try
        {
            using var document = JsonDocument.Parse(completionContent);
            return ParseAdaptationsFromJsonDocument(document);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse DeepSeek adaptations response. Content: {Content}", completionContent);
            return Array.Empty<AdaptationSuggestion>();
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

    private IReadOnlyList<SeriesSuggestion> ParseSeriesFromJsonDocument(JsonDocument document)
    {
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return ParseSeriesSuggestionArray(document.RootElement);
        }

        if (document.RootElement.TryGetProperty("recommendations", out var recommendationsElement) && recommendationsElement.ValueKind == JsonValueKind.Array)
        {
            return ParseSeriesSuggestionArray(recommendationsElement);
        }

        return Array.Empty<SeriesSuggestion>();
    }

    private IReadOnlyList<AdaptationSuggestion> ParseAdaptationsFromJsonDocument(JsonDocument document)
    {
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return ParseAdaptationSuggestionArray(document.RootElement);
        }

        if (document.RootElement.TryGetProperty("recommendations", out var recommendationsElement) && recommendationsElement.ValueKind == JsonValueKind.Array)
        {
            return ParseAdaptationSuggestionArray(recommendationsElement);
        }

        return Array.Empty<AdaptationSuggestion>();
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

    private IReadOnlyList<SeriesSuggestion> ParseSeriesSuggestionArray(JsonElement arrayElement)
    {
        var results = new List<SeriesSuggestion>();
        foreach (var element in arrayElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var title = element.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String
                ? titleElement.GetString()
                : null;

            var installment = element.TryGetProperty("installment", out var instElement) && instElement.ValueKind == JsonValueKind.String
                ? instElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(installment))
            {
                continue;
            }

            string? coverUrl = null;
            if (element.TryGetProperty("coverUrl", out var coverElement) && coverElement.ValueKind == JsonValueKind.String)
            {
                coverUrl = coverElement.GetString();
            }

            string? rationale = null;
            if (element.TryGetProperty("rationale", out var rationaleElement) && rationaleElement.ValueKind == JsonValueKind.String)
            {
                rationale = rationaleElement.GetString();
            }

            decimal? confidence = null;
            if (element.TryGetProperty("confidence", out var confidenceElement) && confidenceElement.ValueKind is JsonValueKind.Number)
            {
                if (confidenceElement.TryGetDecimal(out var value))
                {
                    confidence = value;
                }
            }

            results.Add(new SeriesSuggestion(title!, installment!, coverUrl, rationale, confidence));
        }

        return results;
    }

    private IReadOnlyList<AdaptationSuggestion> ParseAdaptationSuggestionArray(JsonElement arrayElement)
    {
        var results = new List<AdaptationSuggestion>();
        foreach (var element in arrayElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var title = element.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String
                ? titleElement.GetString()
                : null;

            var type = element.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            string? imageUrl = null;
            if (element.TryGetProperty("imageUrl", out var imageElement) && imageElement.ValueKind == JsonValueKind.String)
            {
                imageUrl = imageElement.GetString();
            }

            string? rationale = null;
            if (element.TryGetProperty("rationale", out var rationaleElement) && rationaleElement.ValueKind == JsonValueKind.String)
            {
                rationale = rationaleElement.GetString();
            }

            decimal? confidence = null;
            if (element.TryGetProperty("confidence", out var confidenceElement) && confidenceElement.ValueKind is JsonValueKind.Number)
            {
                if (confidenceElement.TryGetDecimal(out var value))
                {
                    confidence = value;
                }
            }

            results.Add(new AdaptationSuggestion(title!, type!, imageUrl, rationale, confidence));
        }

        return results;
    }

    private string BuildSeriesPrompt(IReadOnlyList<string> titles)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are a literary series recommendation engine.");
        builder.AppendLine("Given the following list of books from the user's library, recommend book series and the next installment to pick up.");
        builder.AppendLine();
        builder.AppendLine("Known books:");
        foreach (var t in titles)
        {
            builder.Append("- ").AppendLine(t);
        }
        builder.AppendLine();
        builder.AppendLine("Return a strict JSON array where each item has the following shape:");
        builder.AppendLine("{");
        builder.AppendLine("  \"title\": string,  // series name");
        builder.AppendLine("  \"installment\": string,  // e.g. 'Book 2', 'Volume 3', 'Part II'");
        builder.AppendLine("  \"coverUrl\": string | null,");
        builder.AppendLine("  \"rationale\": string | null,");
        builder.AppendLine("  \"confidence\": number between 0 and 1");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.Append("Limit the response to ")
            .Append(_options.RecommendationCount)
            .AppendLine(" items. Do not include any text outside of the JSON array.");
        return builder.ToString();
    }

    private string BuildAdaptationsPrompt(IReadOnlyList<string> titles)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You recommend screen adaptations based on books.");
        builder.AppendLine("Given the user's library, suggest film/TV/anime/game adaptations they might enjoy.");
        builder.AppendLine();
        builder.AppendLine("Known books:");
        foreach (var t in titles)
        {
            builder.Append("- ").AppendLine(t);
        }
        builder.AppendLine();
        builder.AppendLine("Return a strict JSON array where each item has the following shape:");
        builder.AppendLine("{");
        builder.AppendLine("  \"title\": string,  // adaptation title");
        builder.AppendLine("  \"type\": string,   // one of 'Movie', 'Series', 'Anime', 'Game'");
        builder.AppendLine("  \"imageUrl\": string | null,");
        builder.AppendLine("  \"rationale\": string | null,");
        builder.AppendLine("  \"confidence\": number between 0 and 1");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.Append("Limit the response to ")
            .Append(_options.RecommendationCount)
            .AppendLine(" items. Do not include any text outside of the JSON array.");
        return builder.ToString();
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
