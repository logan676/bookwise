using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BookWise.Web.Data;
using BookWise.Web.Models;
using BookWise.Web.Options;
using BookWise.Web.Services.Authors;
using BookWise.Web.Services.CommunityContent;
using BookWise.Web.Services.Recommendations;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Configure JSON serialization options to handle circular references
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("DoubanBooks", client =>
{
    client.BaseAddress = new Uri("https://book.douban.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("BookWise/1.0 (+https://bookwise.local)");
});

builder.Services.Configure<DeepSeekOptions>(builder.Configuration.GetSection("DeepSeek"));
builder.Services.AddSingleton<AuthorRecommendationQueue>();
builder.Services.AddSingleton<IAuthorRecommendationScheduler, AuthorRecommendationScheduler>();
builder.Services.AddScoped<IAuthorRecommendationRefresher, AuthorRecommendationRefresher>();
builder.Services.AddHostedService<AuthorRecommendationWorker>();

// Community Content Services
builder.Services.AddSingleton<BookWise.Web.Services.CommunityContent.BookCommunityContentQueue>();
builder.Services.AddSingleton<BookWise.Web.Services.CommunityContent.IBookCommunityContentScheduler, BookWise.Web.Services.CommunityContent.BookCommunityContentScheduler>();
builder.Services.AddScoped<BookWise.Web.Services.CommunityContent.IBookCommunityContentRefresher, BookWise.Web.Services.CommunityContent.BookCommunityContentRefresher>();
builder.Services.AddHostedService<BookWise.Web.Services.CommunityContent.BookCommunityContentWorker>();
builder.Services.AddHttpClient<IDeepSeekRecommendationClient, DeepSeekRecommendationClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<DeepSeekOptions>>().Value;
    if (Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint))
    {
        client.BaseAddress = endpoint;
    }

    client.Timeout = TimeSpan.FromSeconds(45);
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
            CreateSeedBook(
                title: "Atomic Habits",
                authorName: "James Clear",
                description: "An easy & proven way to build good habits and break bad ones.",
                category: "Self-Improvement",
                quote: "Habits are the compound interest of self-improvement.",
                isbn: "9780735211292",
                publicRating: 4.8m),
            CreateSeedBook(
                title: "Project Hail Mary",
                authorName: "Andy Weir",
                description: "A lone astronaut must save the earth from disaster in this epic tale.",
                category: "Science Fiction",
                quote: "Humanity can solve anything when we act together.",
                isbn: "9780593135204",
                publicRating: 4.6m),
            CreateSeedBook(
                title: "Clean Code",
                authorName: "Robert C. Martin",
                description: "A handbook of agile software craftsmanship.",
                category: "Software",
                quote: "Clean code always looks like it was written by someone who cares.",
                isbn: "9780132350884",
                publicRating: 4.7m)
        });

        db.SaveChanges();
    }
}

static Book CreateSeedBook(
    string title,
    string authorName,
    string description,
    string category,
    string quote,
    string isbn,
    decimal publicRating)
{
    var author = new Author
    {
        Name = authorName,
        NormalizedName = AuthorResolver.BuildNormalizedKey(authorName)
    };

    var book = new Book
    {
        Title = title,
        Author = author.Name,
        AuthorDetails = author,
        Category = category,
        Description = description,
        Quote = quote,
        ISBN = isbn,
        PublicRating = publicRating,
        Quotes =
        {
            new BookQuote
            {
                Text = quote,
                Author = author.Name,
                Source = title,
                Origin = BookQuoteSource.Snapshot
            }
        }
    };

    return book;
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

books.MapPost("", async (
    CreateBookRequest request,
    BookWiseContext db,
    ILogger<Program> logger,
    IAuthorRecommendationScheduler recommendationScheduler,
    IBookCommunityContentScheduler communityContentScheduler,
    CancellationToken cancellationToken) =>
{
    var operationId = Guid.NewGuid();
    var timer = Stopwatch.StartNew();

    try
    {
        var normalizedRequest = request.WithNormalizedData();

        logger.LogInformation(
            "[{OperationId}] Creating book with payload: title='{Title}', author='{Author}', status='{Status}', category='{Category}', isbn='{Isbn}', personalRating={PersonalRating}, publicRating={PublicRating}, favorite={Favorite}, quoteProvided={HasQuote}, remarks={RemarksCount}",
            operationId,
            normalizedRequest.Title,
            normalizedRequest.Author,
            normalizedRequest.Status,
            normalizedRequest.Category,
            normalizedRequest.Isbn ?? "N/A",
            normalizedRequest.PersonalRating,
            normalizedRequest.PublicRating,
            normalizedRequest.IsFavorite,
            !string.IsNullOrWhiteSpace(normalizedRequest.Quote),
            normalizedRequest.Remarks?.Length ?? 0);

        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(normalizedRequest, new ValidationContext(normalizedRequest), validationResults, true))
        {
            logger.LogWarning("[{OperationId}] Create validation failed with {Count} errors", operationId, validationResults.Count);
            foreach (var error in validationResults)
            {
                logger.LogWarning("[{OperationId}] Validation error: {ErrorMessage} for members: {Members}", 
                    operationId, error.ErrorMessage, string.Join(", ", error.MemberNames ?? Array.Empty<string>()));
            }
            return Results.ValidationProblem(validationResults
                .GroupBy(r => r.MemberNames.FirstOrDefault() ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.Select(r => r.ErrorMessage ?? string.Empty).ToArray()));
        }

        var author = await AuthorResolver.GetOrCreateAsync(db, normalizedRequest.Author, normalizedRequest.AuthorAvatarUrl, cancellationToken);
        var entity = normalizedRequest.ToEntity(author);
        CreateBookRequest.SyncQuoteSnapshot(entity);
        await db.Books.AddAsync(entity, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        // Schedule async tasks for the created book
        await recommendationScheduler.ScheduleRefreshForAuthorsAsync(new[] { author.Name }, cancellationToken);
        
        // Schedule community content fetch (author info, popular remarks, and quotes)
        await communityContentScheduler.ScheduleFetchAsync(entity.Id, normalizedRequest.DoubanSubjectId, cancellationToken);

        timer.Stop();
        logger.LogInformation(
            "[{OperationId}] Created book with id {Id} in {Elapsed} ms",
            operationId,
            entity.Id,
            timer.ElapsedMilliseconds);
        return Results.Created($"/api/books/{entity.Id}", entity);
    }
    catch (Exception ex)
    {
        timer.Stop();
        logger.LogError(ex, "[{OperationId}] Failed to create book after {Elapsed} ms", operationId, timer.ElapsedMilliseconds);
        return Results.Problem(
            title: "Failed to create book",
            detail: "An error occurred while creating the book. Please try again.",
            statusCode: 500
        );
    }
});

books.MapPut("/{id:int}", async (int id, UpdateBookRequest request, BookWiseContext db, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    var operationId = Guid.NewGuid();
    var timer = Stopwatch.StartNew();

    var normalizedRequest = request.WithNormalizedData();

    logger.LogInformation(
        "[{OperationId}] Updating book {Id} with payload: title='{Title}', author='{Author}', status='{Status}', category='{Category}', isbn='{Isbn}', personalRating={PersonalRating}, publicRating={PublicRating}, favorite={Favorite}, quoteProvided={HasQuote}",
        operationId,
        id,
        normalizedRequest.Title,
        normalizedRequest.Author,
        normalizedRequest.Status,
        normalizedRequest.Category,
        normalizedRequest.Isbn ?? "N/A",
        normalizedRequest.PersonalRating,
        normalizedRequest.PublicRating,
        normalizedRequest.IsFavorite,
        !string.IsNullOrWhiteSpace(normalizedRequest.Quote));

    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(normalizedRequest, new ValidationContext(normalizedRequest), validationResults, true))
    {
        logger.LogWarning("[{OperationId}] Update validation failed for book {Id} with {Count} errors", operationId, id, validationResults.Count);
        return Results.ValidationProblem(validationResults
            .GroupBy(r => r.MemberNames.FirstOrDefault() ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.Select(r => r.ErrorMessage ?? string.Empty).ToArray()));
    }

    var book = await db.Books
        .Include(b => b.Quotes)
        .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    if (book is null)
    {
        logger.LogWarning("[{OperationId}] Book {Id} not found for update", operationId, id);
        return Results.NotFound();
    }

    var author = await AuthorResolver.GetOrCreateAsync(db, normalizedRequest.Author, normalizedRequest.AuthorAvatarUrl, cancellationToken);

    normalizedRequest.Apply(book, author);
    CreateBookRequest.SyncQuoteSnapshot(book);
    await db.SaveChangesAsync(cancellationToken);

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
    var scope = request.GetScope();
    logger.LogInformation(
        "[{OperationId}] Douban search request for query '{Query}' with scope {Scope}",
        operationId,
        request.Query,
        scope);

    if (string.IsNullOrWhiteSpace(request.Query))
    {
        logger.LogWarning("[{OperationId}] Search rejected because query was empty", operationId);
        return Results.BadRequest(new { message = "Query is required." });
    }

    try
    {
        var doubanClient = httpClientFactory.CreateClient("DoubanBooks");
        var books = await DoubanBookSearch.SearchAsync(
            request.Query,
            scope,
            doubanClient,
            logger,
            operationId,
            cancellationToken);

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
    [property: MaxLength(500), Url] string? AuthorAvatarUrl,
    [property: MaxLength(2000)] string? Description,
    [property: MaxLength(500)] string? Quote,
    [property: MaxLength(500), Url] string? CoverImageUrl,
    [property: MaxLength(100)] string? Category,
    [property: MaxLength(20)] string? Isbn,
    [property: MaxLength(32)] string? DoubanSubjectId,
    [property: Required, RegularExpression("^(plan-to-read|reading|read)$", ErrorMessage = "Status must be plan-to-read, reading, or read.")] string Status,
    bool IsFavorite,
    [property: Range(0, 5)] decimal? PersonalRating,
    [property: Range(0, 5)] decimal? PublicRating,
    CreateBookRemark[]? Remarks = null
) : IValidatableObject
{
    public CreateBookRequest WithNormalizedData()
    {
        var normalizedRemarks = NormalizeRemarks(Remarks);

        return this with
        {
            Title = TrimToLength(Title, 200, allowEmpty: true) ?? string.Empty,
            Author = TrimToLength(Author, 200, allowEmpty: true) ?? string.Empty,
            AuthorAvatarUrl = TrimToLength(AuthorAvatarUrl, 500),
            Description = TrimToLength(Description, 2000),
            Quote = TrimToLength(Quote, 500),
            CoverImageUrl = TrimToLength(CoverImageUrl, 500),
            Category = TrimToLength(Category, 100),
            Isbn = NormalizeIsbn(Isbn),
            DoubanSubjectId = NormalizeDoubanSubjectId(DoubanSubjectId),
            Status = NormalizeStatus(Status),
            PersonalRating = NormalizeRating(PersonalRating),
            PublicRating = NormalizeRating(PublicRating),
            Remarks = normalizedRemarks
        };
    }

    public Book ToEntity(Author author)
    {
        ArgumentNullException.ThrowIfNull(author);

        var normalizedTitle = TrimToLength(Title, 200, allowEmpty: true) ?? string.Empty;
        var normalizedAuthor = TrimToLength(author.Name, 200, allowEmpty: true) ?? string.Empty;
        author.Name = normalizedAuthor;
        author.NormalizedName = AuthorResolver.BuildNormalizedKey(normalizedAuthor);
        var normalizedAvatar = TrimToLength(AuthorAvatarUrl, 500);
        if (!string.IsNullOrWhiteSpace(normalizedAvatar))
        {
            author.AvatarUrl = normalizedAvatar;
        }

        var normalizedDescription = TrimToLength(Description, 2000);
        var normalizedQuote = TrimToLength(Quote, 500);
        var normalizedCover = TrimToLength(CoverImageUrl, 500);
        var normalizedCategory = TrimToLength(Category, 100);
        var normalizedIsbn = NormalizeIsbn(Isbn);
        var normalizedDoubanSubjectId = NormalizeDoubanSubjectId(DoubanSubjectId);
        var normalizedStatus = NormalizeStatus(Status);
        var normalizedPersonalRating = NormalizeRating(PersonalRating);
        var normalizedPublicRating = NormalizeRating(PublicRating);
        var entity = new Book
        {
            Title = normalizedTitle,
            Author = normalizedAuthor,
            AuthorId = author.Id,
            AuthorDetails = author,
            Description = normalizedDescription,
            Quote = normalizedQuote,
            CoverImageUrl = normalizedCover,
            Category = normalizedCategory,
            ISBN = normalizedIsbn,
            DoubanSubjectId = normalizedDoubanSubjectId,
            Status = normalizedStatus,
            IsFavorite = IsFavorite,
            PersonalRating = normalizedPersonalRating,
            PublicRating = normalizedPublicRating
        };

        // Add remarks to the entity's collection instead of replacing it
        var remarks = BuildRemarks();
        foreach (var remark in remarks)
        {
            entity.Remarks.Add(remark);
        }

        var quoteSnapshot = CreateQuoteSnapshot(
            normalizedQuote,
            normalizedTitle,
            normalizedAuthor,
            normalizedCover);

        if (quoteSnapshot is not null)
        {
            entity.Quotes.Add(quoteSnapshot);
        }

        return entity;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Remarks is null)
        {
            yield break;
        }

        for (var index = 0; index < Remarks.Length; index++)
        {
            var remark = Remarks[index];
            if (remark is null)
            {
                continue;
            }

            var normalizedRemark = remark.Normalize();
            if (string.IsNullOrWhiteSpace(normalizedRemark.Content))
            {
                continue;
            }

            var validationResults = new List<ValidationResult>();
            var remarkContext = new ValidationContext(normalizedRemark)
            {
                MemberName = $"Remarks[{index}]"
            };

            if (Validator.TryValidateObject(normalizedRemark, remarkContext, validationResults, validateAllProperties: true))
            {
                continue;
            }

            foreach (var result in validationResults)
            {
                var memberNames = result.MemberNames?.Select(member => $"Remarks[{index}].{member}")
                    ?? new[] { $"Remarks[{index}]" };
                yield return new ValidationResult(result.ErrorMessage, memberNames);
            }
        }
    }

    private static string NormalizeStatus(string status) => status.Trim().ToLowerInvariant();

    internal static decimal? NormalizeRating(decimal? rating)
    {
        if (rating is null)
        {
            return null;
        }

        var clamped = Math.Clamp(rating.Value, 0m, 5m);
        return Math.Round(clamped, 1, MidpointRounding.AwayFromZero);
    }

    internal static string? TrimToLength(string? value, int maxLength, bool allowEmpty = false)
    {
        if (value is null)
        {
            return allowEmpty ? string.Empty : null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return allowEmpty ? string.Empty : null;
        }

        if (trimmed.Length > maxLength)
        {
            trimmed = trimmed[..maxLength];
        }

        return trimmed;
    }

    public static string? NormalizeIsbn(string? isbn)
    {
        if (string.IsNullOrWhiteSpace(isbn))
        {
            return null;
        }

        var sequences = new List<string>();
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length > 0)
            {
                sequences.Add(current.ToString());
                current.Clear();
            }
        }

        foreach (var ch in isbn)
        {
            if (char.IsDigit(ch))
            {
                current.Append(ch);
            }
            else if (ch is 'x' or 'X')
            {
                current.Append('X');
            }
            else if (ch is '-' or ' ' or '\u00A0')
            {
                continue;
            }
            else
            {
                Flush();
            }
        }

        Flush();

        if (sequences.Count == 0)
        {
            return null;
        }

        string? TryExtract(int length, bool requirePrefix)
        {
            foreach (var sequence in sequences)
            {
                var candidate = ExtractFromSequence(sequence, length, requirePrefix);
                if (candidate is not null)
                {
                    return candidate;
                }
            }

            return null;
        }

        string? ExtractFromSequence(string sequence, int length, bool requirePrefix)
        {
            if (sequence.Length < length)
            {
                return null;
            }

            for (var index = 0; index <= sequence.Length - length; index++)
            {
                var segment = sequence.Substring(index, length);
                if (requirePrefix && !IsIsbn13Prefix(segment))
                {
                    continue;
                }

                if (length == 10 && !IsValidIsbn10Segment(segment))
                {
                    continue;
                }

                return segment;
            }

            return null;
        }

        static bool IsIsbn13Prefix(string value) => value.Length >= 3 &&
            (value.StartsWith("978", StringComparison.Ordinal) || value.StartsWith("979", StringComparison.Ordinal));

        static bool IsValidIsbn10Segment(string value)
        {
            if (value.Length != 10)
            {
                return false;
            }

            if (!value[..9].All(char.IsDigit))
            {
                return false;
            }

            var last = value[^1];
            return char.IsDigit(last) || last == 'X';
        }

        var isbn13 = TryExtract(13, requirePrefix: true) ?? TryExtract(13, requirePrefix: false);
        if (isbn13 is not null)
        {
            return isbn13;
        }

        var isbn10 = TryExtract(10, requirePrefix: false);
        if (isbn10 is not null)
        {
            return isbn10;
        }

        return null;
    }

    public static string? NormalizeDoubanSubjectId(string? subjectId)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return null;
        }

        var trimmed = subjectId.Trim();
        var filtered = new string(trimmed.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(filtered))
        {
            return null;
        }

        return filtered.Length <= 32 ? filtered : filtered[..32];
    }

    private static CreateBookRemark[]? NormalizeRemarks(CreateBookRemark[]? remarks)
    {
        if (remarks is null || remarks.Length == 0)
        {
            return null;
        }

        var normalized = remarks
            .Where(remark => remark is not null)
            .Select(remark => remark!.Normalize())
            .Where(remark => !string.IsNullOrWhiteSpace(remark.Content))
            .ToArray();

        return normalized.Length == 0 ? null : normalized;
    }

    internal static void SyncQuoteSnapshot(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        var snapshot = CreateQuoteSnapshot(
            book.Quote,
            book.Title,
            book.Author,
            book.CoverImageUrl);

        var existing = book.Quotes.FirstOrDefault(q => q.Origin == BookQuoteSource.Snapshot);

        if (snapshot is null)
        {
            if (existing is not null)
            {
                book.Quotes.Remove(existing);
            }

            return;
        }

        if (existing is null)
        {
            book.Quotes.Add(snapshot);
            return;
        }

        existing.Text = snapshot.Text;
        existing.Author = snapshot.Author;
        existing.Source = snapshot.Source;
        existing.BackgroundImageUrl = snapshot.BackgroundImageUrl;
        existing.Origin = BookQuoteSource.Snapshot;
    }

    internal static BookQuote? CreateQuoteSnapshot(string? quote, string title, string author, string? coverImageUrl)
    {
        if (string.IsNullOrWhiteSpace(quote))
        {
            return null;
        }

        var resolvedAuthor = string.IsNullOrWhiteSpace(author) ? "Unknown" : author;
        var resolvedSource = string.IsNullOrWhiteSpace(title) ? null : title;
        var resolvedCover = string.IsNullOrWhiteSpace(coverImageUrl) ? null : coverImageUrl;

        return new BookQuote
        {
            Text = quote,
            Author = resolvedAuthor,
            Source = resolvedSource,
            Origin = BookQuoteSource.Snapshot,
            BackgroundImageUrl = resolvedCover,
            AddedOn = DateTimeOffset.UtcNow
        };
    }

    private List<BookRemark> BuildRemarks()
    {
        if (Remarks is null || Remarks.Length == 0)
        {
            return new List<BookRemark>();
        }

        var list = new List<BookRemark>(Remarks.Length);
        foreach (var remark in Remarks)
        {
            if (remark is null)
            {
                continue;
            }

            var entity = remark.Normalize().ToEntity();
            if (entity is null)
            {
                continue;
            }

            list.Add(entity);
        }

        return list;
    }
}

record CreateBookRemark(
    [property: MaxLength(200)] string? Title,
    [property: Required, MaxLength(4000)] string Content
)
{
    public CreateBookRemark Normalize()
    {
        var normalizedTitle = CreateBookRequest.TrimToLength(Title, 200);
        var normalizedContent = CreateBookRequest.TrimToLength(Content, 4000) ?? string.Empty;

        return this with
        {
            Title = normalizedTitle,
            Content = normalizedContent
        };
    }

    public BookRemark? ToEntity()
    {
        var normalized = Normalize();
        if (string.IsNullOrWhiteSpace(normalized.Content))
        {
            return null;
        }

        return new BookRemark
        {
            Title = normalized.Title,
            Content = normalized.Content,
            Type = BookRemarkType.Mine,
            AddedOn = DateTimeOffset.UtcNow
        };
    }
}

record UpdateBookRequest(
    [property: Required, MaxLength(200)] string Title,
    [property: Required, MaxLength(200)] string Author,
    [property: MaxLength(500), Url] string? AuthorAvatarUrl,
    [property: MaxLength(2000)] string? Description,
    [property: MaxLength(500)] string? Quote,
    [property: MaxLength(500), Url] string? CoverImageUrl,
    [property: MaxLength(100)] string? Category,
    [property: MaxLength(20)] string? Isbn,
    [property: MaxLength(32)] string? DoubanSubjectId,
    [property: Required, RegularExpression("^(plan-to-read|reading|read)$", ErrorMessage = "Status must be plan-to-read, reading, or read.")] string Status,
    bool IsFavorite,
    [property: Range(0, 5)] decimal? PersonalRating,
    [property: Range(0, 5)] decimal? PublicRating
)
{
    public UpdateBookRequest WithNormalizedData() => this with
    {
        Title = CreateBookRequest.TrimToLength(Title, 200, allowEmpty: true) ?? string.Empty,
        Author = CreateBookRequest.TrimToLength(Author, 200, allowEmpty: true) ?? string.Empty,
        AuthorAvatarUrl = CreateBookRequest.TrimToLength(AuthorAvatarUrl, 500),
        Description = CreateBookRequest.TrimToLength(Description, 2000),
        Quote = CreateBookRequest.TrimToLength(Quote, 500),
        CoverImageUrl = CreateBookRequest.TrimToLength(CoverImageUrl, 500),
        Category = CreateBookRequest.TrimToLength(Category, 100),
        Isbn = CreateBookRequest.NormalizeIsbn(Isbn),
        DoubanSubjectId = CreateBookRequest.NormalizeDoubanSubjectId(DoubanSubjectId),
        Status = NormalizeStatus(Status),
        PersonalRating = CreateBookRequest.NormalizeRating(PersonalRating),
        PublicRating = CreateBookRequest.NormalizeRating(PublicRating)
    };

    public void Apply(Book book, Author author)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(author);

        var normalizedAuthor = CreateBookRequest.TrimToLength(author.Name, 200, allowEmpty: true) ?? string.Empty;
        author.Name = normalizedAuthor;
        author.NormalizedName = AuthorResolver.BuildNormalizedKey(normalizedAuthor);
        var normalizedAvatar = CreateBookRequest.TrimToLength(AuthorAvatarUrl, 500);
        if (!string.IsNullOrWhiteSpace(normalizedAvatar))
        {
            author.AvatarUrl = normalizedAvatar;
        }

        book.Title = CreateBookRequest.TrimToLength(Title, 200, allowEmpty: true) ?? string.Empty;
        book.Author = normalizedAuthor;
        book.AuthorId = author.Id;
        book.AuthorDetails = author;
        book.Description = CreateBookRequest.TrimToLength(Description, 2000);
        book.Quote = CreateBookRequest.TrimToLength(Quote, 500);
        book.CoverImageUrl = CreateBookRequest.TrimToLength(CoverImageUrl, 500);
        book.Category = CreateBookRequest.TrimToLength(Category, 100);
        book.ISBN = CreateBookRequest.NormalizeIsbn(Isbn);
        book.DoubanSubjectId = CreateBookRequest.NormalizeDoubanSubjectId(DoubanSubjectId);
        book.Status = NormalizeStatus(Status);
        book.IsFavorite = IsFavorite;
        book.PersonalRating = CreateBookRequest.NormalizeRating(PersonalRating);
        book.PublicRating = CreateBookRequest.NormalizeRating(PublicRating);
    }

    private static string NormalizeStatus(string status) => status.Trim().ToLowerInvariant();
}

record BookSearchRequest(string Query, string? SearchBy = null)
{
    public BookSearchScope GetScope()
    {
        if (string.Equals(SearchBy, "author", StringComparison.OrdinalIgnoreCase))
        {
            return BookSearchScope.Author;
        }

        if (string.Equals(SearchBy, "title", StringComparison.OrdinalIgnoreCase))
        {
            return BookSearchScope.Title;
        }

        return BookSearchScope.Auto;
    }
}

record BookSearchResponse(BookSuggestion[] Books);

enum BookSearchScope
{
    Auto,
    Title,
    Author
}

record BookSuggestion(
    string Title,
    string Author,
    string? Description,
    string? Quote,
    string? Category,
    string? Isbn,
    decimal? Rating,
    string? Published,
    string? Language,
    string? CoverImageUrl,
    string? DoubanSubjectId
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
    private const int AuthorSuggestionLimit = 15;
    private const int AutoSuggestionLimit = MaxResults * 2;
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly (string Category, string[] Keywords)[] CategoryMappings = new[]
    {
        ("Science Fiction", new[] { "science fiction", "sci-fi", "sci fi", "科幻", "太空" }),
        ("Fantasy", new[] { "fantasy", "奇幻", "魔幻" }),
        ("History", new[] { "history", "histor", "历史", "文明" }),
        ("Biography", new[] { "biography", "memoir", "传记" }),
        ("Self-Improvement", new[] { "self-help", "self help", "习惯", "成长", "motivation", "心理" }),
        ("Technology", new[] { "programming", "software", "编码", "开发", "算法", "coding", "工程" }),
        ("Business", new[] { "business", "管理", "经济", "finance", "投资" }),
        ("Philosophy", new[] { "philosophy", "哲学" }),
        ("Poetry", new[] { "poetry", "诗" }),
        ("Literature & Fiction", new[] { "fiction", "novel", "小说", "文学" })
    };

    public static async Task<BookSuggestion[]> SearchAsync(
        string query,
        BookSearchScope scope,
        HttpClient client,
        ILogger logger,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var suggestionCandidates = await FetchSuggestionsAsync(
            query,
            scope,
            client,
            logger,
            operationId,
            cancellationToken);

        if (suggestionCandidates.Count == 0)
        {
            return Array.Empty<BookSuggestion>();
        }

        var results = new List<BookSuggestion>(suggestionCandidates.Count);

        foreach (var suggestion in suggestionCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var book = await FetchBookDetailsAsync(suggestion, client, logger, operationId, cancellationToken);
            if (book is { } detail)
            {
                if (scope == BookSearchScope.Author && !AuthorMatchesQuery(detail.Author, query))
                {
                    continue;
                }

                results.Add(detail);

                if (results.Count >= MaxResults)
                {
                    break;
                }
            }
        }

        return results.ToArray();
    }

    private static string? ExtractDoubanSubjectId(Uri detailUri)
    {
        if (detailUri is null)
        {
            return null;
        }

        var segments = detailUri.Segments;
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index].Trim('/');
            if (!segment.Equals("subject", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= segments.Length)
            {
                break;
            }

            var candidate = segments[index + 1].Trim('/');
            var filtered = new string(candidate.Where(char.IsLetterOrDigit).ToArray());
            if (!string.IsNullOrWhiteSpace(filtered))
            {
                return filtered;
            }
        }

        var path = detailUri.AbsolutePath;
        var match = Regex.Match(path, @"subject/(\w+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static async Task<List<DoubanSuggestion>> FetchSuggestionsAsync(
        string query,
        BookSearchScope scope,
        HttpClient client,
        ILogger logger,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (scope == BookSearchScope.Auto)
        {
            return await FetchSuggestionsForAutoAsync(
                query,
                client,
                logger,
                operationId,
                cancellationToken);
        }

        var suggestionLimit = scope == BookSearchScope.Author ? AuthorSuggestionLimit : MaxResults;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<DoubanSuggestion>(suggestionLimit);
        var queries = scope == BookSearchScope.Author
            ? BuildAuthorQueryVariants(query)
            : BuildTitleQueryVariants(query);

        foreach (var candidate in queries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var encodedQuery = Uri.EscapeDataString(candidate);
            var requestUri = $"j/subject_suggest?q={encodedQuery}";

            using var response = await client.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "[{OperationId}] Douban suggestion request failed with status {StatusCode} for query '{Candidate}'",
                    operationId,
                    (int)response.StatusCode,
                    candidate);
                continue;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                logger.LogWarning("[{OperationId}] Douban suggestion response was not an array", operationId);
                continue;
            }

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

                if (!seen.Add(url.AbsoluteUri))
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

                results.Add(new DoubanSuggestion(title, author, year, url));

                if (results.Count >= suggestionLimit)
                {
                    return results;
                }
            }
        }

        return results;
    }

    private static async Task<List<DoubanSuggestion>> FetchSuggestionsForAutoAsync(
        string query,
        HttpClient client,
        ILogger logger,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var combined = new List<DoubanSuggestion>(AutoSuggestionLimit);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static string KeyFor(DoubanSuggestion suggestion) => suggestion.DetailUri.AbsoluteUri;

        var titleSuggestions = await FetchSuggestionsAsync(
            query,
            BookSearchScope.Title,
            client,
            logger,
            operationId,
            cancellationToken);

        foreach (var suggestion in titleSuggestions)
        {
            if (seen.Add(KeyFor(suggestion)))
            {
                combined.Add(suggestion);
                if (combined.Count >= AutoSuggestionLimit)
                {
                    return combined;
                }
            }
        }

        if (combined.Count >= AutoSuggestionLimit)
        {
            return combined;
        }

        var authorSuggestions = await FetchSuggestionsAsync(
            query,
            BookSearchScope.Author,
            client,
            logger,
            operationId,
            cancellationToken);

        foreach (var suggestion in authorSuggestions)
        {
            if (seen.Add(KeyFor(suggestion)))
            {
                combined.Add(suggestion);
                if (combined.Count >= AutoSuggestionLimit)
                {
                    break;
                }
            }
        }

        return combined;
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
                ? (string.IsNullOrWhiteSpace(suggestion.Title) ? "Untitled" : suggestion.Title)
                : parsedBook.Title;

            var author = string.IsNullOrWhiteSpace(parsedBook.Author)
                ? (string.IsNullOrWhiteSpace(suggestion.Author) ? "未知作者" : suggestion.Author)
                : parsedBook.Author;

            var description = string.IsNullOrWhiteSpace(parsedBook.Description)
                ? null
                : parsedBook.Description;

            var quote = string.IsNullOrWhiteSpace(parsedBook.Quote)
                ? null
                : parsedBook.Quote;

            var category = string.IsNullOrWhiteSpace(parsedBook.Category)
                ? null
                : parsedBook.Category;

            var isbn = string.IsNullOrWhiteSpace(parsedBook.Isbn)
                ? null
                : parsedBook.Isbn;

            var rating = parsedBook.Rating;

            var published = string.IsNullOrWhiteSpace(parsedBook.Published)
                ? suggestion.Year
                : parsedBook.Published;

            var language = string.IsNullOrWhiteSpace(parsedBook.Language)
                ? null
                : parsedBook.Language;

            var cover = string.IsNullOrWhiteSpace(parsedBook.CoverImageUrl)
                ? null
                : parsedBook.CoverImageUrl;

            var subjectId = ExtractDoubanSubjectId(suggestion.DetailUri);

            return new BookSuggestion(
                title,
                string.IsNullOrWhiteSpace(author) ? "未知作者" : author,
                description,
                quote,
                category,
                isbn,
                rating,
                string.IsNullOrWhiteSpace(published) ? null : published,
                language,
                cover,
                subjectId);
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
        var isbn = CreateBookRequest.NormalizeIsbn(GetInfoValue(document, "ISBN"));
        var quote = ExtractQuote(document);
        var category = InferCategory(document, title, description);
        var rating = ParseRating(document);

        var sanitizedTitle = string.IsNullOrWhiteSpace(title) ? "Untitled" : HtmlEntity.DeEntitize(title);
        var sanitizedAuthor = string.IsNullOrWhiteSpace(author) ? "未知作者" : HtmlEntity.DeEntitize(author);
        var sanitizedDescription = CreateBookRequest.TrimToLength(string.IsNullOrWhiteSpace(description) ? null : HtmlEntity.DeEntitize(description), 2000);
        var sanitizedQuote = CreateBookRequest.TrimToLength(string.IsNullOrWhiteSpace(quote) ? null : HtmlEntity.DeEntitize(quote), 500);
        var sanitizedCategory = CreateBookRequest.TrimToLength(string.IsNullOrWhiteSpace(category) ? null : HtmlEntity.DeEntitize(category), 100);
        var sanitizedPublished = string.IsNullOrWhiteSpace(published) ? null : HtmlEntity.DeEntitize(published);
        var sanitizedLanguage = string.IsNullOrWhiteSpace(language) ? null : HtmlEntity.DeEntitize(language);
        var sanitizedCover = string.IsNullOrWhiteSpace(cover) ? null : NormalizeCoverUrl(cover);

        return new BookSuggestion(
            sanitizedTitle,
            sanitizedAuthor,
            sanitizedDescription,
            sanitizedQuote,
            sanitizedCategory,
            string.IsNullOrWhiteSpace(isbn) ? null : isbn,
            rating,
            sanitizedPublished,
            sanitizedLanguage,
            sanitizedCover,
            null);
    }

    private static decimal? ParseRating(HtmlDocument document)
    {
        var ratingNode = document.DocumentNode.SelectSingleNode("//strong[contains(@class,'rating_num')]");
        if (ratingNode is null)
        {
            return null;
        }

        var text = NormalizeWhitespace(HtmlEntity.DeEntitize(ratingNode.InnerText));
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var raw) || raw <= 0)
        {
            return null;
        }

        var scaled = raw / 2m;
        if (scaled < 0)
        {
            scaled = 0;
        }
        else if (scaled > 5)
        {
            scaled = 5;
        }

        return Math.Round(scaled, 1, MidpointRounding.AwayFromZero);
    }

    private static string? ExtractQuote(HtmlDocument document)
    {
        var linkReport = document.DocumentNode.SelectSingleNode("//div[@id='link-report']");
        var paragraphs = linkReport?.SelectNodes(".//p");
        if (paragraphs is null)
        {
            return null;
        }

        string? fallback = null;
        foreach (var paragraph in paragraphs)
        {
            var raw = HtmlEntity.DeEntitize(paragraph.InnerText);
            var text = NormalizeWhitespace(raw);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            fallback ??= text;

            if (text.Contains('“') || text.Contains('”') || text.Contains('"') || text.Contains('「') || text.Contains('」') || text.Contains("——"))
            {
                return text;
            }
        }

        return fallback;
    }

    private static string? InferCategory(HtmlDocument document, string title, string? description)
    {
        static string? SanitizeCategory(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = NormalizeWhitespace(value);
            return CreateBookRequest.TrimToLength(normalized, 100);
        }

        var tagNode = document.DocumentNode.SelectSingleNode("//div[@id='db-tags-section']//a");
        var tagCandidate = SanitizeCategory(HtmlEntity.DeEntitize(tagNode?.InnerText ?? string.Empty));
        if (tagCandidate is not null)
        {
            return tagCandidate;
        }

        var seriesCandidate = SanitizeCategory(GetInfoValue(document, "丛书"));
        if (seriesCandidate is not null)
        {
            return seriesCandidate;
        }

        var normalized = (title + " " + (description ?? string.Empty)).ToLowerInvariant();

        foreach (var (category, keywords) in CategoryMappings)
        {
            foreach (var keyword in keywords)
            {
                if (normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return CreateBookRequest.TrimToLength(category, 100);
                }
            }
        }

        var publisherCandidate = SanitizeCategory(GetInfoValue(document, "出版社"));
        if (publisherCandidate is not null)
        {
            return publisherCandidate;
        }

        var producerCandidate = SanitizeCategory(GetInfoValue(document, "出品方"));
        if (producerCandidate is not null)
        {
            return producerCandidate;
        }

        return null;
    }

    private static IEnumerable<string> BuildTitleQueryVariants(string query)
    {
        var trimmed = (query ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            yield break;
        }

        yield return trimmed;
    }

    private static IEnumerable<string> BuildAuthorQueryVariants(string query)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = (query ?? string.Empty).Trim();

        if (!string.IsNullOrEmpty(trimmed) && seen.Add(trimmed))
        {
            yield return trimmed;
        }

        var tokens = ExtractSearchTokens(trimmed);
        if (tokens.Length > 1)
        {
            var joined = string.Join(' ', tokens);
            if (!string.IsNullOrEmpty(joined) && seen.Add(joined))
            {
                yield return joined;
            }
        }

        foreach (var token in tokens.OrderByDescending(token => token.Length))
        {
            if (seen.Add(token))
            {
                yield return token;
            }
        }
    }

    private static string[] ExtractSearchTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        var separators = new[] { ' ', ',', '.', ';', ':', '-', '_', '/', '\\', '\'', '"', '|', '\u00B7', '\u2013', '\u2014' };
        return value
            .Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => new string(part.Where(char.IsLetterOrDigit).ToArray()))
            .Where(token => token.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool AuthorMatchesQuery(string? author, string query)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            return false;
        }

        var normalizedAuthor = NormalizeForMatch(author);
        if (string.IsNullOrEmpty(normalizedAuthor))
        {
            return false;
        }

        var normalizedQuery = NormalizeForMatch(query);
        if (!string.IsNullOrEmpty(normalizedQuery) &&
            normalizedAuthor.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var token in ExtractSearchTokens(query))
        {
            var normalizedToken = NormalizeForMatch(token);
            if (!string.IsNullOrEmpty(normalizedToken) &&
                normalizedAuthor.Contains(normalizedToken, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeForMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousIsSeparator = false;

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                previousIsSeparator = false;
            }
            else
            {
                if (!previousIsSeparator && builder.Length > 0)
                {
                    builder.Append(' ');
                    previousIsSeparator = true;
                }
            }
        }

        return builder.ToString().Trim();
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

            var builder = new StringBuilder();
            for (var node = span.NextSibling; node is not null; node = node.NextSibling)
            {
                if (string.Equals(node.Name, "br", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (node.NodeType == HtmlNodeType.Element && string.Equals(node.GetAttributeValue("class", string.Empty), "pl", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var segment = HtmlEntity.DeEntitize(node.InnerText);
                segment = NormalizeWhitespace(segment);

                if (string.IsNullOrWhiteSpace(segment))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(segment);
            }

            var cleaned = NormalizeWhitespace(builder.ToString());
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
