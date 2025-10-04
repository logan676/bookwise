using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using BookWise.Web.Models;
using BookWise.Web.Data;
using BookWise.Web.Services.Authors;

namespace BookWise.Web.Pages
{
    public class AddBookModel : PageModel
    {
        private readonly BookWiseContext _context;
        private readonly ILogger<AddBookModel> _logger;

        public AddBookModel(BookWiseContext context, ILogger<AddBookModel> logger)
        {
            _context = context;
            _logger = logger;
        }

        [BindProperty]
        public string Title { get; set; } = string.Empty;

        [BindProperty]
        public string Author { get; set; } = string.Empty;

        [BindProperty]
        public string? ISBN { get; set; }

        [BindProperty]
        public string? Category { get; set; }

        [BindProperty]
        public string? Quote { get; set; }

        [BindProperty]
        public string? Description { get; set; }

        [BindProperty]
        public string? CoverImageUrl { get; set; }

        [BindProperty]
        public decimal? PersonalRating { get; set; }

        [BindProperty]
        public decimal? PublicRating { get; set; }

        [BindProperty]
        public string Status { get; set; } = "plan-to-read";

        [BindProperty]
        public bool IsFavorite { get; set; }

        public void OnGet()
        {
            _logger.LogInformation("[AddBook] GET request received");
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var timer = Stopwatch.StartNew();
            _logger.LogInformation("[AddBook] POST request to create '{Title}' by {Author} with status '{Status}'", Title, Author, Status);
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("[AddBook] Model state invalid with {ErrorCount} errors", ModelState.ErrorCount);
                return Page();
            }

            var cancellationToken = HttpContext.RequestAborted;

            var request = new CreateBookRequest(
                Title,
                Author,
                Description,
                Quote,
                CoverImageUrl,
                Category,
                ISBN,
                Status,
                IsFavorite,
                PersonalRating,
                PublicRating,
                Remarks: null);

            var normalized = request.WithNormalizedData();
            var authorEntity = await AuthorResolver.GetOrCreateAsync(_context, normalized.Author, cancellationToken);
            var book = normalized.ToEntity(authorEntity);

            _context.Books.Add(book);
            await _context.SaveChangesAsync(cancellationToken);

            timer.Stop();
            _logger.LogInformation("[AddBook] Book created with id {Id} in {Elapsed} ms", book.Id, timer.ElapsedMilliseconds);
            return RedirectToPage("./Index");
        }
    }
}
