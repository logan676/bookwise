using System.ComponentModel.DataAnnotations;
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
    client.Timeout = TimeSpan.FromSeconds(15); // Shorter timeout for faster feedback
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    Proxy = null,
    UseProxy = false,
    ConnectTimeout = TimeSpan.FromSeconds(5), // Faster connection timeout
    PooledConnectionLifetime = TimeSpan.FromMinutes(2) // Connection lifetime
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

books.MapGet("", async ([AsParameters] BookQuery query, BookWiseContext db) =>
{
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

    return Results.Ok(results);
});

books.MapGet("/{id:int}", async (int id, BookWiseContext db) =>
{
    var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
    return book is null ? Results.NotFound() : Results.Ok(book);
});

books.MapPost("", async (CreateBookRequest request, BookWiseContext db) =>
{
    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(request, new ValidationContext(request), validationResults, true))
    {
        return Results.ValidationProblem(validationResults
            .GroupBy(r => r.MemberNames.FirstOrDefault() ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.Select(r => r.ErrorMessage ?? string.Empty).ToArray()));
    }

    var entity = request.ToEntity();
    await db.Books.AddAsync(entity);
    await db.SaveChangesAsync();

    return Results.Created($"/api/books/{entity.Id}", entity);
});

books.MapPut("/{id:int}", async (int id, UpdateBookRequest request, BookWiseContext db) =>
{
    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(request, new ValidationContext(request), validationResults, true))
    {
        return Results.ValidationProblem(validationResults
            .GroupBy(r => r.MemberNames.FirstOrDefault() ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.Select(r => r.ErrorMessage ?? string.Empty).ToArray()));
    }

    var book = await db.Books.FindAsync(id);
    if (book is null)
    {
        return Results.NotFound();
    }

    request.Apply(book);
    await db.SaveChangesAsync();

    return Results.Ok(book);
});

books.MapDelete("/{id:int}", async (int id, BookWiseContext db) =>
{
    var book = await db.Books.FindAsync(id);
    if (book is null)
    {
        return Results.NotFound();
    }

    db.Books.Remove(book);
    await db.SaveChangesAsync();

    return Results.NoContent();
});

// Fallback search endpoint that doesn't rely on external APIs
app.MapPost("/api/book-search-fallback", ([FromBody] BookSearchRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Query))
    {
        return Results.BadRequest(new { message = "Query is required." });
    }

    var suggestions = GetChineseFallbackSuggestions(request.Query);
    return Results.Ok(new BookSearchResponse(suggestions));
});

app.MapPost("/api/book-search", async Task<IResult> (
    [FromBody] BookSearchRequest request,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) =>
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

    if (string.IsNullOrWhiteSpace(request.Query))
    {
        return Results.BadRequest(new { message = "Query is required." });
    }

    var apiKey = configuration["DeepSeek:ApiKey"] ?? configuration["DEEPSEEK_API_KEY"];
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return Results.Problem("DeepSeek API key is not configured.", statusCode: StatusCodes.Status500InternalServerError);
    }

    var model = configuration["DeepSeek:Model"] ?? "deepseek-chat"; // Use faster chat model instead of reasoner

    var httpClient = httpClientFactory.CreateClient("DeepSeek");
    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    var requestBody = new
    {
        model,
        temperature = 0.3, // Lower temperature for more consistent results
        max_tokens = 1000, // Limit response size for faster processing
        messages = new object[]
        {
            new
            {
                role = "system",
                content = "Return book search results as JSON only. Format: {\"books\":[{\"title\":\"string\",\"author\":\"string\",\"description\":\"string\",\"publishedYear\":number}]}. Be concise."
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
        using var httpContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync("chat/completions", httpContent);

        if (!response.IsSuccessStatusCode)
        {
            var details = await response.Content.ReadAsStringAsync();
            return Results.Problem($"DeepSeek request failed: {details}", statusCode: (int)response.StatusCode);
        }

        using var responseStream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(responseStream);
        if (!document.RootElement.TryGetProperty("choices", out var choicesElement) || choicesElement.ValueKind != JsonValueKind.Array || choicesElement.GetArrayLength() == 0)
        {
            return Results.Ok(new { books = Array.Empty<BookSuggestion>() });
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
            return Results.Ok(new { books = Array.Empty<BookSuggestion>() });
        }

        var booksJson = ExtractJsonObject(content);
        if (string.IsNullOrWhiteSpace(booksJson))
        {
            return Results.Ok(new { books = Array.Empty<BookSuggestion>() });
        }

        try
        {
            var suggestions = JsonSerializer.Deserialize<BookSearchResponse>(booksJson, JsonOptions.Instance);
            return Results.Ok(suggestions ?? new BookSearchResponse(Array.Empty<BookSuggestion>()));
        }
        catch (JsonException)
        {
            return Results.Ok(new { books = Array.Empty<BookSuggestion>() });
        }
    }
    catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
    {
        // Return fallback suggestions for Chinese search terms when API times out
        if (IsChineseText(request.Query))
        {
            var fallbackSuggestions = GetChineseFallbackSuggestions(request.Query);
            return Results.Ok(new BookSearchResponse(fallbackSuggestions));
        }
        return Results.Problem("Search request timed out. Please try a simpler search term.", statusCode: StatusCodes.Status408RequestTimeout);
    }
    catch (TaskCanceledException)
    {
        // Return fallback suggestions for Chinese search terms when API is canceled
        if (IsChineseText(request.Query))
        {
            var fallbackSuggestions = GetChineseFallbackSuggestions(request.Query);
            return Results.Ok(new BookSearchResponse(fallbackSuggestions));
        }
        return Results.Problem("Search request was canceled. Please try again.", statusCode: StatusCodes.Status408RequestTimeout);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem($"Network error while searching: {ex.Message}", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        return Results.Problem($"An error occurred while searching: {ex.Message}", statusCode: StatusCodes.Status500InternalServerError);
    }
});

// Helper functions for fallback search suggestions
static bool IsChineseText(string text)
{
    if (string.IsNullOrWhiteSpace(text)) return false;
    return text.Any(c => c >= 0x4E00 && c <= 0x9FFF); // Basic Chinese characters range
}

static BookSuggestion[] GetChineseFallbackSuggestions(string query)
{
    // Provide some common Chinese book suggestions as fallback
    var suggestions = new List<BookSuggestion>();
    
    if (query.Contains("一地鸡毛"))
    {
        suggestions.Add(new BookSuggestion("一地鸡毛", "刘震云", "当代中国文学作品，描述了改革开放后的中国社会变迁。", "1992", "Chinese", null));
        suggestions.Add(new BookSuggestion("一句顶一万句", "刘震云", "刘震云的另一部代表作品。", "2009", "Chinese", null));
    }
    else if (query.Contains("红楼梦"))
    {
        suggestions.Add(new BookSuggestion("红楼梦", "曹雪芹", "中国古典四大名著之一。", "1791", "Chinese", null));
    }
    else if (query.Contains("西游记"))
    {
        suggestions.Add(new BookSuggestion("西游记", "吴承恩", "中国古典四大名著之一。", "1592", "Chinese", null));
    }
    else
    {
        // Generic Chinese literature suggestions
        suggestions.AddRange(new[]
        {
            new BookSuggestion("活着", "余华", "当代中国文学经典作品。", "1993", "Chinese", null),
            new BookSuggestion("围城", "钱钟书", "现代中国文学名著。", "1947", "Chinese", null),
            new BookSuggestion("平凡的世界", "路遥", "茅盾文学奖获奖作品。", "1986", "Chinese", null)
        });
    }
    
    return suggestions.Take(3).ToArray();
}

app.Run();

record BookQuery(string? Search, bool OnlyFavorites = false, string? Category = null);

record CreateBookRequest(
    [property: Required, MaxLength(200)] string Title,
    [property: Required, MaxLength(200)] string Author,
    [property: MaxLength(2000)] string? Description,
    [property: MaxLength(500), Url] string? CoverImageUrl,
    [property: MaxLength(100)] string? Category,
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
        IsFavorite = IsFavorite,
        Rating = Rating
    };
}

record UpdateBookRequest(
    [property: Required, MaxLength(200)] string Title,
    [property: Required, MaxLength(200)] string Author,
    [property: MaxLength(2000)] string? Description,
    [property: MaxLength(500), Url] string? CoverImageUrl,
    [property: MaxLength(100)] string? Category,
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
        book.IsFavorite = IsFavorite;
        book.Rating = Rating;
    }
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
