using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using BookWise.Web.Data;
using BookWise.Web.Models;
using HtmlAgilityPack;
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
builder.Services.AddHttpClient("DoubanBooks", client =>
{
    client.BaseAddress = new Uri("https://book.douban.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("BookWise/1.0 (+https://bookwise.local)");
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
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var operationId = Guid.NewGuid();
    var searchTimer = Stopwatch.StartNew();
    logger.LogInformation("[{OperationId}] Douban search request for query '{Query}'", operationId, request.Query);

    if (string.IsNullOrWhiteSpace(request.Query))
    {
        logger.LogWarning("[{OperationId}] Search rejected because query was empty", operationId);
        return Results.BadRequest(new { message = "Query is required." });
    }

    try
    {
        var doubanClient = httpClientFactory.CreateClient("DoubanBooks");
        var books = await DoubanBookSearch.SearchAsync(request.Query, doubanClient, logger, operationId, cancellationToken);

        logger.LogInformation("[{OperationId}] Douban search returned {Count} results in {Elapsed} ms",
            operationId,
            books.Length,
            searchTimer.ElapsedMilliseconds);

        return Results.Ok(new BookSearchResponse(books));
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        logger.LogWarning("[{OperationId}] Douban search was canceled", operationId);
        return Results.Problem("Search request was canceled. Please try again.", statusCode: StatusCodes.Status408RequestTimeout);
    }
    catch (TaskCanceledException ex)
    {
        logger.LogError(ex, "[{OperationId}] Douban search timed out", operationId);
        return Results.Problem("Search request timed out. Please try again.", statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (HttpRequestException ex)
    {
        logger.LogError(ex, "[{OperationId}] Network error during Douban search", operationId);
        return Results.Problem($"Network error while searching: {ex.Message}", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (JsonException ex)
    {
        logger.LogError(ex, "[{OperationId}] Failed to parse Douban response", operationId);
        return Results.Problem($"Failed to parse Douban response: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[{OperationId}] Unexpected error during Douban search", operationId);
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

static class DoubanBookSearch
{
    private const int MaxResults = 5;
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static async Task<BookSuggestion[]> SearchAsync(
        string query,
        HttpClient client,
        ILogger logger,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var suggestions = await FetchSuggestionsAsync(query, client, logger, operationId, cancellationToken);
        if (suggestions.Count == 0)
        {
            return Array.Empty<BookSuggestion>();
        }

        var results = new List<BookSuggestion>(suggestions.Count);

        foreach (var suggestion in suggestions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var book = await FetchBookDetailsAsync(suggestion, client, logger, operationId, cancellationToken);
            if (book is { } detail)
            {
                results.Add(detail);
            }
        }

        return results.ToArray();
    }

    private static async Task<List<DoubanSuggestion>> FetchSuggestionsAsync(
        string query,
        HttpClient client,
        ILogger logger,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var encodedQuery = Uri.EscapeDataString(query); // Douban expects UTF-8 query
        var requestUri = $"j/subject_suggest?q={encodedQuery}";

        using var response = await client.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("[{OperationId}] Douban suggestion request failed with status {StatusCode}", operationId, (int)response.StatusCode);
            return new List<DoubanSuggestion>(); // no suggestions available
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            logger.LogWarning("[{OperationId}] Douban suggestion response was not an array", operationId);
            return new List<DoubanSuggestion>();
        }

        var suggestions = new List<DoubanSuggestion>(MaxResults);

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (!element.TryGetProperty("url", out var urlElement) || urlElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var urlText = urlElement.GetString();
            if (string.IsNullOrWhiteSpace(urlText))
            {
                continue;
            }

            if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url))
            {
                continue;
            }

            var title = element.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String
                ? titleElement.GetString() ?? string.Empty
                : string.Empty;

            var author = element.TryGetProperty("author_name", out var authorElement) && authorElement.ValueKind == JsonValueKind.String
                ? authorElement.GetString()
                : null;

            var year = element.TryGetProperty("year", out var yearElement) && yearElement.ValueKind == JsonValueKind.String
                ? yearElement.GetString()
                : null;

            suggestions.Add(new DoubanSuggestion(title, author, year, url));

            if (suggestions.Count >= MaxResults)
            {
                break;
            }
        }

        return suggestions;
    }

    private static async Task<BookSuggestion?> FetchBookDetailsAsync(
        DoubanSuggestion suggestion,
        HttpClient client,
        ILogger logger,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(suggestion.DetailUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("[{OperationId}] Douban detail request for '{Title}' failed with status {StatusCode}", operationId, suggestion.Title, (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();

            var parsed = ParseBookDetails(html);
            if (parsed is null)
            {
                logger.LogWarning("[{OperationId}] Unable to parse Douban book page for '{Title}'", operationId, suggestion.Title);
                return null;
            }

            var parsedBook = parsed;

            var title = string.IsNullOrWhiteSpace(parsedBook.Title)
                ? suggestion.Title
                : parsedBook.Title;

            var author = string.IsNullOrWhiteSpace(parsedBook.Author)
                ? (suggestion.Author ?? "未知作者")
                : parsedBook.Author;

            var description = string.IsNullOrWhiteSpace(parsedBook.Description)
                ? null
                : parsedBook.Description;

            var published = string.IsNullOrWhiteSpace(parsedBook.Published)
                ? suggestion.Year
                : parsedBook.Published;

            var language = string.IsNullOrWhiteSpace(parsedBook.Language)
                ? null
                : parsedBook.Language;

            var cover = string.IsNullOrWhiteSpace(parsedBook.CoverImageUrl)
                ? null
                : parsedBook.CoverImageUrl;

            return new BookSuggestion(
                title,
                string.IsNullOrWhiteSpace(author) ? "未知作者" : author,
                description,
                string.IsNullOrWhiteSpace(published) ? null : published,
                language,
                cover);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{OperationId}] Failed to fetch Douban details for '{Title}'", operationId, suggestion.Title);
            return null;
        }
    }

    private static BookSuggestion? ParseBookDetails(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var title = FirstNonEmpty(
            GetMetaContent(document, "og:title"),
            GetJsonLdValue(document, "name"));

        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var description = NormalizeWhitespace(FirstNonEmpty(
            GetMetaContent(document, "og:description"),
            GetInfoValue(document, "内容简介")));

        var author = JoinNonEmpty(GetMetaContents(document, "book:author"));
        if (string.IsNullOrWhiteSpace(author))
        {
            author = GetInfoValue(document, "作者");
        }

        var published = GetInfoValue(document, "出版年");
        var language = GetInfoValue(document, "语言");
        var cover = UpgradeDoubanImageUrl(GetMetaContent(document, "og:image"));

        return new BookSuggestion(
            HtmlEntity.DeEntitize(title),
            string.IsNullOrWhiteSpace(author) ? "未知作者" : HtmlEntity.DeEntitize(author),
            string.IsNullOrWhiteSpace(description) ? null : HtmlEntity.DeEntitize(description),
            string.IsNullOrWhiteSpace(published) ? null : HtmlEntity.DeEntitize(published),
            string.IsNullOrWhiteSpace(language) ? null : HtmlEntity.DeEntitize(language),
            string.IsNullOrWhiteSpace(cover) ? null : NormalizeCoverUrl(cover));
    }

    private static string? GetMetaContent(HtmlDocument document, string property)
    {
        var node = document.DocumentNode.SelectSingleNode($"//meta[@property='{property}']");
        var value = node?.GetAttributeValue("content", null);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string[] GetMetaContents(HtmlDocument document, string property)
    {
        var nodes = document.DocumentNode.SelectNodes($"//meta[@property='{property}']");
        if (nodes is null)
        {
            return Array.Empty<string>();
        }

        return nodes
            .Select(node => node.GetAttributeValue("content", null))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? GetJsonLdValue(HtmlDocument document, string property)
    {
        var scriptNode = document.DocumentNode.SelectSingleNode("//script[@type='application/ld+json']");
        if (scriptNode is null)
        {
            return null;
        }

        try
        {
            using var jsonDocument = JsonDocument.Parse(scriptNode.InnerText);
            if (jsonDocument.RootElement.TryGetProperty(property, out var valueElement) && valueElement.ValueKind == JsonValueKind.String)
            {
                return valueElement.GetString();
            }
        }
        catch (JsonException)
        {
            // Fall back silently; some pages embed invalid JSON
        }

        return null;
    }

    private static string? GetInfoValue(HtmlDocument document, string label)
    {
        var nodes = document.DocumentNode.SelectNodes("//div[@id='info']//span[@class='pl']");
        if (nodes is null)
        {
            return null;
        }

        foreach (var span in nodes)
        {
            var text = HtmlEntity.DeEntitize(span.InnerText).Trim();
            text = text.TrimEnd(':', '：').Trim();

            if (!string.Equals(text, label, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parent = span.ParentNode;
            if (parent is null)
            {
                continue;
            }

            var rawValue = HtmlEntity.DeEntitize(parent.InnerText);
            rawValue = rawValue.Replace(span.InnerText, string.Empty, StringComparison.Ordinal);
            var cleaned = NormalizeWhitespace(rawValue);
            cleaned = cleaned.Trim(':', '：');

            return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
        }

        return null;
    }

    private static string JoinNonEmpty(IEnumerable<string> values)
    {
        return string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()));
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return WhitespaceRegex.Replace(value, " ").Trim();
    }

    private static string? UpgradeDoubanImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var upgraded = url;
        if (upgraded.Contains("/s/", StringComparison.Ordinal))
        {
            upgraded = upgraded.Replace("/s/", "/l/", StringComparison.Ordinal);
        }

        if (upgraded.Contains("/small/", StringComparison.Ordinal))
        {
            upgraded = upgraded.Replace("/small/", "/large/", StringComparison.Ordinal);
        }

        return upgraded;
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

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private readonly record struct DoubanSuggestion(string Title, string? Author, string? Year, Uri DetailUri);
}
