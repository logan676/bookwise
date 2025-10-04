using BookWise.Web.Data;
using BookWise.Web.Models;
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

    public BookDetailViewModel Book { get; private set; } = BookDetailViewModel.CreateDefault();

    public async Task OnGetAsync(int? id)
    {
        var detail = BookDetailViewModel.CreateDefault();

        if (id.HasValue)
        {
            var entity = await _context.Books
                .AsNoTracking()
                .Include(b => b.Remarks)
                .FirstOrDefaultAsync(b => b.Id == id.Value);
            if (entity is not null)
            {
                detail.Title = string.IsNullOrWhiteSpace(entity.Title) ? detail.Title : entity.Title;
                detail.Author = string.IsNullOrWhiteSpace(entity.Author) ? detail.Author : entity.Author;
                detail.CoverImageUrl = string.IsNullOrWhiteSpace(entity.CoverImageUrl)
                    ? detail.CoverImageUrl
                    : entity.CoverImageUrl;

                if (!string.IsNullOrWhiteSpace(entity.Description))
                {
                    detail.Description = entity.Description;
                }

                if (!string.IsNullOrWhiteSpace(entity.Category))
                {
                    detail.Category = entity.Category;
                }

                if (entity.Rating.HasValue)
                {
                    detail.Rating = entity.Rating.Value;
                }

                if (entity.Remarks?.Count > 0)
                {
                    detail.MyRemarks = entity.Remarks
                        .Where(r => r.Type == BookRemarkType.Mine)
                        .OrderByDescending(r => r.AddedOn)
                        .Select(r => new BookRemarkViewModel(
                            r.Title ?? "Personal Remark",
                            r.AddedOn.UtcDateTime,
                            r.Content))
                        .ToList();

                    detail.CommunityRemarks = entity.Remarks
                        .Where(r => r.Type == BookRemarkType.Community)
                        .OrderByDescending(r => r.AddedOn)
                        .Select(r => new BookRemarkViewModel(
                            r.Title ?? "Community Remark",
                            r.AddedOn.UtcDateTime,
                            r.Content))
                        .ToList();
                }
            }
        }

        Book = detail;
        ViewData["Title"] = detail.Title;
        ViewData["MainClass"] = "main-content--wide book-detail-shell";
    }
}

public class BookDetailViewModel
{
    public string Title { get; set; } = "Unknown Title";
    public string Author { get; set; } = "Unknown Author";
    public string CoverImageUrl { get; set; } = string.Empty;
    public DateTime PublishedOn { get; set; }
        = new(2022, 1, 15);
    public string Language { get; set; } = "English";
    public int PageCount { get; set; } = 0;
    public string? Category { get; set; }
        = "Mystery";
    public string? Description { get; set; }
        = "This book was a thrilling read! The plot twists kept me guessing until the very end.";
    public decimal? Rating { get; set; }
        = null;
    public List<BookRemarkViewModel> MyRemarks { get; set; } = new();
    public List<BookRemarkViewModel> CommunityRemarks { get; set; } = new();
    public List<BookQuote> Quotes { get; set; } = new();
    public List<BookStatusEntry> StatusHistory { get; set; } = new();

    public string PublishedOnDisplay => PublishedOn == DateTime.MinValue
        ? "-"
        : PublishedOn.ToString("MMMM d, yyyy");

    public string PageCountDisplay => PageCount > 0 ? PageCount.ToString() : "-";

    public static BookDetailViewModel CreateDefault()
    {
        return new BookDetailViewModel
        {
            Title = "The Silent Observer",
            Author = "Amelia Stone",
            CoverImageUrl = "https://images.unsplash.com/photo-1544947950-fa07a98d237f?auto=format&fit=crop&w=420&q=80",
            PublishedOn = new DateTime(2022, 1, 15),
            Language = "English",
            PageCount = 320,
            MyRemarks = new List<BookRemarkViewModel>
            {
                new(
                    "My Thoughts",
                    new DateTime(2023, 3, 10),
                    "This book was a thrilling read! The plot twists kept me guessing until the very end. Highly recommend for mystery lovers.")
            },
            CommunityRemarks = new List<BookRemarkViewModel>
            {
                new(
                    "Staff Recommendation",
                    new DateTime(2023, 3, 9),
                    "Our local book club loved discussing the intricate motives in chapter twelve.")
            },
            Quotes = new List<BookQuote>
            {
                new("The truth is a whisper; not a shout. You must learn to listen.", new DateTime(2023, 4, 5)),
                new("A shadow can only exist where there is light.", new DateTime(2023, 4, 2))
            },
            StatusHistory = new List<BookStatusEntry>
            {
                new("read", "Read", "Completed on March 20, 2023"),
                new("reading", "In Reading", "Started on March 10, 2023"),
                new("plan", "Want to Read", "Added on February 28, 2023")
            }
        };
    }
}

public record BookRemarkViewModel(string Title, DateTime AddedOn, string Content);

public record BookQuote(string Text, DateTime AddedOn);

public record BookStatusEntry(string Type, string Label, string Description);
