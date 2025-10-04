using System.Diagnostics;
using BookWise.Web.Data;
using BookWise.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BookWise.Web.Pages;

public class IndexModel : PageModel
{
    private readonly BookWiseContext _context;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(BookWiseContext context, ILogger<IndexModel> logger)
    {
        _context = context;
        _logger = logger;
    }

    public List<Book> CurrentlyReading { get; set; } = new();
    public List<Book> AlreadyRead { get; set; } = new();
    public List<Book> PlanToRead { get; set; } = new();
    public bool HasAnyBooks { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var timer = Stopwatch.StartNew();
        _logger.LogInformation("[Index] GET request to load library");
        var allBooks = await _context.Books.ToListAsync();

        // Check if user has any books
        HasAnyBooks = allBooks.Any();

        // If no books exist, redirect to Add Book page
        if (!HasAnyBooks)
        {
            timer.Stop();
            _logger.LogInformation("[Index] No books found, redirecting to AddBook page in {Elapsed} ms", timer.ElapsedMilliseconds);
            return RedirectToPage("/AddBook");
        }

        // Group books by status
        _logger.LogDebug("[Index] Categorising {Count} books", allBooks.Count);
        CurrentlyReading = allBooks.Where(b => b.Status == "reading").ToList();
        AlreadyRead = allBooks.Where(b => b.Status == "read").ToList();
        PlanToRead = allBooks.Where(b => b.Status == "plan-to-read").ToList();

        timer.Stop();
        _logger.LogInformation(
            "[Index] Page ready in {Elapsed} ms: {Reading} reading, {Read} read, {Plan} plan-to-read",
            timer.ElapsedMilliseconds,
            CurrentlyReading.Count,
            AlreadyRead.Count,
            PlanToRead.Count);

        return Page();
    }
}
