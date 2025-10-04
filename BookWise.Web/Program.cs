using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BookWise.Web.Data;
using BookWise.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("DeepSeek", client =>
{
    client.BaseAddress = new Uri("https://api.deepseek.com/v1/");
    client.Timeout = TimeSpan.FromSeconds(120);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    Proxy = null,
    UseProxy = false,
    ConnectTimeout = TimeSpan.FromSeconds(30),
    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
});

builder.Services.AddHttpClient("GoogleBooks", client =>
{
    client.BaseAddress = new Uri("https://www.googleapis.com/books/v1/");
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddDbContext<BookWiseContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BookWiseContext>();
    var connectionString = scope.ServiceProvider.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        var sqliteBuilder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = sqliteBuilder.DataSource;
        var absolutePath = Path.IsPathRooted(dataSource)
            ? dataSource
            : Path.Combine(app.Environment.ContentRootPath, dataSource);
        var directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    db.Database.Migrate();

    if (!db.Books.Any())
    {
        db.Books.AddRange(new[]
        {
            new Book
            {
                Title = "Atomic Habits",
                Author = "James Clear",
                Description = "An easy & proven way to build good habits and break bad ones.",
                Category = "Self-Improvement",
                Rating = 4.8m
            },
            new Book
            {
                Title = "Project Hail Mary",
                Author = "Andy Weir",
                Description = "A lone astronaut must save the earth from disaster in this epic tale.",
                Category = "Science Fiction",
                Rating = 4.6m
            },
            new Book
            {
                Title = "Clean Code",
                Author = "Robert C. Martin",
                Description = "A handbook of agile software craftsmanship.",
                Category = "Software",
                Rating = 4.7m
            }
        });

        db.SaveChanges();
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

var books = app.MapGroup("/api/books");

books.MapGet("", async ([AsParameters] BookQuery query, BookWiseContext db, ILogger<Program> logger) =>
{
    var operationId = Guid.NewGuid();
    var timer = Stopwatch.StartNew();
    logger.LogInformation(
        "[{OperationId}] Listing books with parameters: search='{Search}', onlyFavorites={OnlyFavorites}, category='{Category}'",
        operationId,
        query.Search,
        query.OnlyFavorites,
        query.Category);

    var booksQuery = db.Books.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(query.Search))
    {
        var term = query.Search.Trim().ToLowerInvariant();
        booksQuery = booksQuery.Where(b =>
            EF.Functions.Like(b.Title.ToLower(), $"%{term}%") ||
            EF.Functions.Like(b.Author.ToLower(), $"%{term}%"));
    }

    if (query.OnlyFavorites)
    {
        booksQuery = booksQuery.Where(b => b.IsFavorite);
    }

    if (!string.IsNullOrWhiteSpace(query.Category))
    {
        var category = query.Category.Trim().ToLowerInvariant();
        booksQuery = booksQuery.Where(b => b.Category != null && b.Category.ToLower() == category);
    }

    var results = await booksQuery
        .OrderBy(b => b.Title)
        .Take(25)
        .ToListAsync();

    timer.Stop();
    logger.LogInformation(
        "[{OperationId}] Completed list request with {Count} results in {Elapsed} ms",
        operationId,
        results.Count,
        timer.ElapsedMilliseconds);
    return Results.Ok(results);
});

books.MapGet("/{id:int}", async (int id, BookWiseContext db, ILogger<Program> logger) =>
{
    var operationId = Guid.NewGuid();
    var timer = Stopwatch.StartNew();
    logger.LogInformation("[{OperationId}] Fetching book {Id}", operationId, id);
    var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
    if (book is null)
    {
        logger.LogWarning("[{OperationId}] Book {Id} not found", operationId, id);
        return Results.NotFound();
    }

    timer.Stop();
    logger.LogInformation(
        "[{OperationId}] Returning book {Id} (title='{Title}', author='{Author}') in {Elapsed} ms",
        operationId,
        id,
        book.Title,
        book.Author,
        timer.ElapsedMilliseconds);
    return Results.Ok(book);
});

books.MapPost("", async (CreateBookRequest request, BookWiseContext db, ILogger<Program> logger) =>
{
    var operationId = Guid.NewGuid();
    var timer = Stopwatch.StartNew();
    logger.LogInformation(
        "[{OperationId}] Creating book with payload: title='{Title}', author='{Author}', status='{Status}', category='{Category}', rating={Rating}, favorite={Favorite}",
        operationId,
        request.Title,
        request.Author,
        request.Status,
        request.Category,
        request.Rating,
        request.IsFavorite);

    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(request, new ValidationContext(request), validationResults, true))
    {
        logger.LogWarning("[{OperationId}] Create validation failed with {Count} errors", operationId, validationResults.Count);
        return Results.ValidationProblem(validationResults
            .GroupBy(r => r.MemberNames.FirstOrDefault() ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.Select(r => r.ErrorMessage ?? string.Empty).ToArray()));
    }

    var entity = request.ToEntity();
    await db.Books.AddAsync(entity);
    await db.SaveChangesAsync();

    timer.Stop();
    logger.LogInformation(
        "[{OperationId}] Created book with id {Id} in {Elapsed} ms",
        operationId,
        entity.Id,
        timer.ElapsedMilliseconds);
    return Results.Created($"/api/books/{entity.Id}", entity);
});

books.MapPut("/{id:int}", async (int id, UpdateBookRequest request, BookWiseContext db, ILogger<Program> logger) =>
{
    var operationId = Guid.NewGuid();
    var timer = Stopwatch.StartNew();
    logger.LogInformation(
        "[{OperationId}] Updating book {Id} with payload: title='{Title}', author='{Author}', status='{Status}', category='{Category}', rating={Rating}, favorite={Favorite}",
        operationId,
        id,
        request.Title,
        request.Author,
        request.Status,
        request.Category,
        request.Rating,
        request.IsFavorite);

    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(request, new ValidationContext(request), validationResults, true))
    {
        logger.LogWarning("[{OperationId}] Update validation failed for book {Id} with {Count} errors", operationId, id, validationResults.Count);
        return Results.ValidationProblem(validationResults
            .GroupBy(r => r.MemberNames.FirstOrDefault() ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.Select(r => r.ErrorMessage ?? string.Empty).ToArray()));
    }

    var book = await db.Books.FindAsync(id);
    if (book is null)
    {
        logger.LogWarning("[{OperationId}] Book {Id} not found for update", operationId, id);
        return Results.NotFound();
    }

    request.Apply(book);
    await db.SaveChangesAsync();

    timer.Stop();
    logger.LogInformation(
        "[{OperationId}] Updated book {Id} (title='{Title}', status='{Status}') in {Elapsed} ms",
        operationId,
        id,
        book.Title,
        book.Status,
        timer.ElapsedMilliseconds);
    return Results.Ok(book);
});

books.MapDelete("/{id:int}", async (int id, BookWiseContext db, ILogger<Program> logger) =>
{
    var operationId = Guid.NewGuid();
    var timer = Stopwatch.StartNew();
    logger.LogInformation("[{OperationId}] Deleting book {Id}", operationId, id);
    var book = await db.Books.FindAsync(id);
    if (book is null)
    {
        logger.LogWarning("[{OperationId}] Book {Id} not found for delete", operationId, id);
        return Results.NotFound();
    }

    db.Books.Remove(book);
    await db.SaveChangesAsync();

    timer.Stop();
    logger.LogInformation(
        "[{OperationId}] Deleted book {Id} (title='{Title}') in {Elapsed} ms",
        operationId,
        id,
        book.Title,
        timer.ElapsedMilliseconds);
    return Results.NoContent();
});

app.MapPost("/api/book-search", async Task<IResult> (
    [FromBody] BookSearchRequest request,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    static string? ExtractJsonObject(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value
            .Replace("<think>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</think>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var endFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (endFence > 0)
            {
                text = text[3..endFence].Trim();
                if (text.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    text = text[4..].TrimStart();
                }
            }
        }

        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');

        if (firstBrace < 0 || lastBrace <= firstBrace)
        {
            return null;
        }

        var jsonCandidate = text.Substring(firstBrace, lastBrace - firstBrace + 1).Trim();
        return string.IsNullOrWhiteSpace(jsonCandidate) ? null : jsonCandidate;
    }

    var operationId = Guid.NewGuid();
    var searchTimer = Stopwatch.StartNew();
    logger.LogInformation("[{OperationId}] Incoming book search request for query '{Query}'", operationId, request.Query);

    if (string.IsNullOrWhiteSpace(request.Query))
    {
        logger.LogWarning("[{OperationId}] Search rejected because query was empty", operationId);
        return Results.BadRequest(new { message = "Query is required." });
    }

    var apiKey = configuration["DeepSeek:ApiKey"] ?? configuration["DEEPSEEK_API_KEY"];
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return Results.Problem("DeepSeek API key is not configured.", statusCode: StatusCodes.Status500InternalServerError);
    }

    var model = configuration["DeepSeek:Model"] ?? "deepseek-reasoner";

    var httpClient = httpClientFactory.CreateClient("DeepSeek");
    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    var requestBody = new
    {
        model,
        temperature = 0.3,
        max_tokens = 2500,
        stream = false,
        response_format = new
        {
            type = "json_object"
        },
        messages = new object[]
        {
            new
            {
                role = "system",
                content = "Respond with pure JSON (no markdown, no code fences). Format exactly as {\"books\":[{\"title\":string,\"author\":string,\"description\":string,\"published\":string|null,\"language\":string|null,\"coverImageUrl\":string|null}]}."
            },
            new
            {
                role = "user",
                content = $"Find 3-5 books matching '{request.Query}'. Return JSON only."
            }
        }
    };

    try
    {
        logger.LogDebug("[{OperationId}] Submitting request to DeepSeek", operationId);
        using var httpContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync("chat/completions", httpContent);

        if (!response.IsSuccessStatusCode)
        {
            var details = await response.Content.ReadAsStringAsync();
            logger.LogError("[{OperationId}] DeepSeek request failed with status {StatusCode}: {Details}", operationId, (int)response.StatusCode, details);
            return Results.Problem($"DeepSeek request failed: {details}", statusCode: (int)response.StatusCode);
        }

        using var responseStream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(responseStream);
        if (!document.RootElement.TryGetProperty("choices", out var choicesElement) ||
            choicesElement.ValueKind != JsonValueKind.Array || choicesElement.GetArrayLength() == 0)
        {
            logger.LogError("[{OperationId}] DeepSeek response missing choices array", operationId);
            return Results.Problem("DeepSeek response did not include any choices.", statusCode: StatusCodes.Status502BadGateway);
        }

        var content = choicesElement
            .EnumerateArray()
            .Select(choice =>
            {
                if (choice.TryGetProperty("message", out var messageElement) &&
                    messageElement.TryGetProperty("content", out var contentElement))
                {
                    if (contentElement.ValueKind == JsonValueKind.String)
                    {
                        return contentElement.GetString();
                    }

                    if (contentElement.ValueKind == JsonValueKind.Array)
                    {
                        return string.Join(string.Empty,
                            contentElement.EnumerateArray()
                                .Select(part =>
                                {
                                    if (part.ValueKind == JsonValueKind.String)
                                    {
                                        return part.GetString();
                                    }

                                    if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var textPart))
                                    {
                                        return textPart.GetString();
                                    }

                                    return null;
                                })
                                .Where(segment => !string.IsNullOrEmpty(segment)));
                    }
                }

                return (string?)null;
            })
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .LastOrDefault();

        if (string.IsNullOrWhiteSpace(content))
        {
            logger.LogError("[{OperationId}] DeepSeek response content empty", operationId);
            return Results.Problem("DeepSeek response was empty.", statusCode: StatusCodes.Status502BadGateway);
        }

        var booksJson = ExtractJsonObject(content);
        if (string.IsNullOrWhiteSpace(booksJson))
        {
            logger.LogError("[{OperationId}] Failed to extract JSON object from DeepSeek response", operationId);
            return Results.Problem("DeepSeek response did not contain valid JSON output.", statusCode: StatusCodes.Status502BadGateway);
        }

        BookSearchResponse? suggestions;
        try
        {
            suggestions = JsonSerializer.Deserialize<BookSearchResponse>(booksJson, JsonOptions.Instance);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "[{OperationId}] DeepSeek returned invalid JSON", operationId);
            return Results.Problem($"DeepSeek returned invalid JSON: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
        }

        if (suggestions?.Books is not { Length: >= 0 })
        {
            logger.LogError("[{OperationId}] DeepSeek response missing books collection", operationId);
            return Results.Problem("DeepSeek response was missing the books collection.", statusCode: StatusCodes.Status502BadGateway);
        }

        logger.LogInformation("[{OperationId}] DeepSeek returned {Count} book suggestions", operationId, suggestions.Books.Length);

        var enriched = await BookSearchEnrichment.EnrichCoverImagesAsync(suggestions.Books, httpClientFactory, logger, operationId, cancellationToken);

        logger.LogInformation("[{OperationId}] Completed search pipeline in {Elapsed} ms", operationId, searchTimer.ElapsedMilliseconds);
        return Results.Ok(new BookSearchResponse(enriched));
    }
    catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
    {
        logger.LogError(ex, "[{OperationId}] Search request timed out", operationId);
        return Results.Problem("Search request timed out. Please try again.", statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (TaskCanceledException)
    {
        logger.LogWarning("[{OperationId}] Search request was canceled", operationId);
        return Results.Problem("Search request was canceled. Please try again.", statusCode: StatusCodes.Status408RequestTimeout);
    }
    catch (HttpRequestException ex)
    {
        logger.LogError(ex, "[{OperationId}] Network error during search", operationId);
        return Results.Problem($"Network error while searching: {ex.Message}", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[{OperationId}] Unexpected error during search", operationId);
        return Results.Problem($"An error occurred while searching: {ex.Message}", statusCode: StatusCodes.Status500InternalServerError);
    }
    finally
    {
        searchTimer.Stop();
    }
});

app.Run();

record BookQuery(string? Search, bool OnlyFavorites = false, string? Category = null);

record CreateBookRequest(
    [property: Required, MaxLength(200)] string Title,
    [property: Required, MaxLength(200)] string Author,
    [property: MaxLength(2000)] string? Description,
    [property: MaxLength(500), Url] string? CoverImageUrl,
    [property: MaxLength(100)] string? Category,
    [property: Required, RegularExpression("^(plan-to-read|reading|read)$", ErrorMessage = "Status must be plan-to-read, reading, or read.")] string Status,
    bool IsFavorite,
    [property: Range(0, 5)] decimal? Rating
)
{
    public Book ToEntity() => new()
    {
        Title = Title.Trim(),
        Author = Author.Trim(),
        Description = Description?.Trim(),
        CoverImageUrl = CoverImageUrl?.Trim(),
        Category = Category?.Trim(),
        Status = NormalizeStatus(Status),
        IsFavorite = IsFavorite,
        Rating = Rating
    };

    private static string NormalizeStatus(string status) => status.Trim().ToLowerInvariant();
}

record UpdateBookRequest(
    [property: Required, MaxLength(200)] string Title,
    [property: Required, MaxLength(200)] string Author,
    [property: MaxLength(2000)] string? Description,
    [property: MaxLength(500), Url] string? CoverImageUrl,
    [property: MaxLength(100)] string? Category,
    [property: Required, RegularExpression("^(plan-to-read|reading|read)$", ErrorMessage = "Status must be plan-to-read, reading, or read.")] string Status,
    bool IsFavorite,
    [property: Range(0, 5)] decimal? Rating
)
{
    public void Apply(Book book)
    {
        book.Title = Title.Trim();
        book.Author = Author.Trim();
        book.Description = Description?.Trim();
        book.CoverImageUrl = CoverImageUrl?.Trim();
        book.Category = Category?.Trim();
        book.Status = NormalizeStatus(Status);
        book.IsFavorite = IsFavorite;
        book.Rating = Rating;
    }

    private static string NormalizeStatus(string status) => status.Trim().ToLowerInvariant();
}

record BookSearchRequest(string Query);

record BookSearchResponse(BookSuggestion[] Books);

record BookSuggestion(
    string Title,
    string Author,
    string? Description,
    string? Published,
    string? Language,
    string? CoverImageUrl
);

static class JsonOptions
{
    public static readonly JsonSerializerOptions Instance = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}

static class BookSearchEnrichment
{
    public static async Task<BookSuggestion[]> EnrichCoverImagesAsync(
        BookSuggestion[] books,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (books.Length == 0)
        {
            return books;
        }

        var client = httpClientFactory.CreateClient("GoogleBooks");
        var enriched = new BookSuggestion[books.Length];

        for (var i = 0; i < books.Length; i++)
        {
            var book = books[i];
            if (!string.IsNullOrWhiteSpace(book.CoverImageUrl))
            {
                logger.LogDebug("[{OperationId}] Skipping cover lookup for '{Title}' – already has cover", operationId, book.Title);
                enriched[i] = book;
                continue;
            }

            logger.LogDebug("[{OperationId}] Fetching cover for '{Title}' by {Author}", operationId, book.Title, book.Author);
            var coverUrl = await TryFetchCoverUrlAsync(client, book, logger, operationId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(coverUrl))
            {
                logger.LogInformation("[{OperationId}] Found cover for '{Title}'", operationId, book.Title);
                enriched[i] = book with { CoverImageUrl = coverUrl };
            }
            else
            {
                logger.LogWarning("[{OperationId}] No cover found for '{Title}'", operationId, book.Title);
                enriched[i] = book;
            }
        }

        return enriched;
    }

    private static async Task<string?> TryFetchCoverUrlAsync(
        HttpClient client,
        BookSuggestion book,
        ILogger logger,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var querySegments = new List<string>();

        if (!string.IsNullOrWhiteSpace(book.Title))
        {
            querySegments.Add($"intitle:{book.Title}");
        }

        if (!string.IsNullOrWhiteSpace(book.Author))
        {
            querySegments.Add($"inauthor:{book.Author}");
        }

        if (querySegments.Count == 0)
        {
            logger.LogWarning("[{OperationId}] Skipping cover lookup because query segments were empty", operationId);
            return null;
        }

        var requestUri = $"volumes?q={Uri.EscapeDataString(string.Join(" ", querySegments))}&maxResults=1&printType=books&fields=items(volumeInfo/imageLinks/thumbnail,imageLinks/smallThumbnail)";

        try
        {
            using var response = await client.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("[{OperationId}] Google Books returned status {StatusCode} for '{Title}'", operationId, (int)response.StatusCode, book.Title);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("items", out var itemsElement) ||
                itemsElement.ValueKind != JsonValueKind.Array ||
                itemsElement.GetArrayLength() == 0)
            {
                logger.LogDebug("[{OperationId}] Google Books returned no items for '{Title}'", operationId, book.Title);
                return null;
            }

            var firstItem = itemsElement[0];
            if (!firstItem.TryGetProperty("volumeInfo", out var volumeInfo) ||
                volumeInfo.ValueKind != JsonValueKind.Object)
            {
                logger.LogDebug("[{OperationId}] Google Books response missing volumeInfo for '{Title}'", operationId, book.Title);
                return null;
            }

            if (!volumeInfo.TryGetProperty("imageLinks", out var imageLinks) ||
                imageLinks.ValueKind != JsonValueKind.Object)
            {
                logger.LogDebug("[{OperationId}] Google Books response missing imageLinks for '{Title}'", operationId, book.Title);
                return null;
            }

            static string? ReadImageLink(JsonElement element)
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (element.TryGetProperty("thumbnail", out var thumb) && thumb.ValueKind == JsonValueKind.String)
                {
                    return thumb.GetString();
                }

                if (element.TryGetProperty("smallThumbnail", out var small) && small.ValueKind == JsonValueKind.String)
                {
                    return small.GetString();
                }

                return null;
            }

            var link = ReadImageLink(imageLinks);
            return NormalizeCoverUrl(link);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("[{OperationId}] Cover lookup cancelled for '{Title}'", operationId, book.Title);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{OperationId}] Failed to fetch cover for '{Title}'", operationId, book.Title);
            return null;
        }
    }

    private static string? NormalizeCoverUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var trimmed = url.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed[7..];
        }

        return trimmed.Replace("&edge=curl", string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
