using System.ComponentModel.DataAnnotations;
using BookWise.Web.Data;
using BookWise.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

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
