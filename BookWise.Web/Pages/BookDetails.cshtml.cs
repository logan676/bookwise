using BookWise.Web.Data;
using BookWise.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BookWise.Web.Pages;

public class BookDetailsModel : PageModel
{
    private readonly BookWiseContext _context;

    public BookDetailsModel(BookWiseContext context)
    {
        _context = context;
    }

    public BookDetailViewModel Book { get; private set; } = BookDetailViewModel.Empty;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (!id.HasValue)
        {
            return RedirectToPage("/Index");
        }

        var entity = await _context.Books
            .AsNoTracking()
            .Include(b => b.Remarks)
            .FirstOrDefaultAsync(b => b.Id == id.Value);

        if (entity is null)
        {
            return NotFound();
        }

        Book = BookDetailViewModel.FromEntity(entity);
        ViewData["Title"] = Book.Title;
        ViewData["MainClass"] = "main-content--wide book-detail-shell";

        return Page();
    }
}

public class BookDetailViewModel
{
    private static readonly Dictionary<string, string> StatusLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["plan-to-read"] = "Want to read",
        ["reading"] = "Currently reading",
        ["read"] = "Finished reading"
    };

    private BookDetailViewModel()
    {
    }

    public int Id { get; private init; }
    public string Title { get; private init; } = string.Empty;
    public string Author { get; private init; } = string.Empty;
    public string? Description { get; private init; }
    public string? CoverImageUrl { get; private init; }
    public string? Category { get; private init; }
    public string? Quote { get; private init; }
    public string? ISBN { get; private init; }
    public string Status { get; private init; } = "plan-to-read";
    public bool IsFavorite { get; private init; }
    public decimal? PersonalRating { get; private init; }
    public decimal? PublicRating { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset? UpdatedAt { get; private init; }
    public IReadOnlyList<BookRemarkViewModel> MyRemarks { get; private init; } = Array.Empty<BookRemarkViewModel>();
    public IReadOnlyList<BookRemarkViewModel> CommunityRemarks { get; private init; } = Array.Empty<BookRemarkViewModel>();

    public static BookDetailViewModel Empty { get; } = new();

    public string DisplayCoverImageUrl => string.IsNullOrWhiteSpace(CoverImageUrl)
        ? "/img/book-placeholder.svg"
        : CoverImageUrl;

    public string DescriptionOrPlaceholder => string.IsNullOrWhiteSpace(Description)
        ? "This book does not have a description yet."
        : Description!;

    public string CategoryOrPlaceholder => string.IsNullOrWhiteSpace(Category)
        ? "Uncategorized"
        : Category!;

    public string? QuoteOrNull => string.IsNullOrWhiteSpace(Quote) ? null : Quote;

    public string ISBNDisplay => string.IsNullOrWhiteSpace(ISBN) ? "—" : ISBN!;

    public string StatusDisplay => StatusLabels.TryGetValue(Status, out var label)
        ? label
        : Status;

    public string StatusToken
    {
        get
        {
            var normalized = string.IsNullOrWhiteSpace(Status)
                ? string.Empty
                : Status.Trim().ToLowerInvariant();

            return StatusLabels.ContainsKey(Status)
                ? normalized
                : string.IsNullOrEmpty(normalized) ? "custom" : normalized.Replace(' ', '-');
        }
    }

    public bool HasPersonalRating => PersonalRating.HasValue;

    public string PersonalRatingDisplay => PersonalRating.HasValue ? $"{PersonalRating:0.0}" : "—";

    public bool HasPublicRating => PublicRating.HasValue;

    public string PublicRatingDisplay => PublicRating.HasValue ? $"{PublicRating:0.0}" : "—";

    public int PersonalRatingPercent => PersonalRating.HasValue
        ? (int)Math.Round((double)(PersonalRating.Value / 5m * 100m))
        : 0;

    public int PublicRatingPercent => PublicRating.HasValue
        ? (int)Math.Round((double)(PublicRating.Value / 5m * 100m))
        : 0;

    public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("MMMM d, yyyy");

    public string UpdatedAtDisplay => UpdatedAt.HasValue
        ? UpdatedAt.Value.ToLocalTime().ToString("MMMM d, yyyy")
        : "—";

    public static BookDetailViewModel FromEntity(Book entity)
    {
        var remarks = entity.Remarks ?? new List<BookRemark>();

        var myRemarks = remarks
            .Where(r => r.Type == BookRemarkType.Mine)
            .OrderByDescending(r => r.AddedOn)
            .Select(r => new BookRemarkViewModel(
                string.IsNullOrWhiteSpace(r.Title) ? "Personal Remark" : r.Title!,
                r.AddedOn.UtcDateTime,
                r.Content))
            .ToList();

        var communityRemarks = remarks
            .Where(r => r.Type == BookRemarkType.Community)
            .OrderByDescending(r => r.AddedOn)
            .Select(r => new BookRemarkViewModel(
                string.IsNullOrWhiteSpace(r.Title) ? "Community Remark" : r.Title!,
                r.AddedOn.UtcDateTime,
                r.Content))
            .ToList();

        var status = string.IsNullOrWhiteSpace(entity.Status)
            ? "plan-to-read"
            : entity.Status.Trim();

        return new BookDetailViewModel
        {
            Id = entity.Id,
            Title = entity.Title,
            Author = entity.Author,
            Description = entity.Description,
            CoverImageUrl = entity.CoverImageUrl,
            Category = entity.Category,
            Quote = entity.Quote,
            ISBN = entity.ISBN,
            Status = status,
            IsFavorite = entity.IsFavorite,
            PersonalRating = entity.PersonalRating,
            PublicRating = entity.PublicRating,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            MyRemarks = myRemarks,
            CommunityRemarks = communityRemarks
        };
    }
}

public record BookRemarkViewModel(string Title, DateTime AddedOn, string Content);
