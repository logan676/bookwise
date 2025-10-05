using System.Diagnostics;
using System.ComponentModel.DataAnnotations;
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
        public string? AuthorAvatarUrl { get; set; }

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
                foreach (var error in ModelState)
                {
                    _logger.LogWarning("[AddBook] Model error for {Key}: {Errors}", error.Key, string.Join("; ", error.Value.Errors.Select(e => e.ErrorMessage)));
                }
                return Page();
            }

            try
            {
                var cancellationToken = HttpContext.RequestAborted;

                var request = new CreateBookRequest(
                    Title,
                    Author,
                    AuthorAvatarUrl,
                    Description,
                    Quote,
                    CoverImageUrl,
                    Category,
                    Publisher: null, // Not supported in this form
                    ISBN,
                    DoubanSubjectId: null, // Not supported in this form
                    Status,
                    IsFavorite,
                    PersonalRating,
                    PublicRating,
                    Remarks: null);

                _logger.LogDebug("[AddBook] Validating request...");
                var validationResults = new List<ValidationResult>();
                var validationContext = new ValidationContext(request);
                if (!Validator.TryValidateObject(request, validationContext, validationResults, validateAllProperties: true))
                {
                    _logger.LogWarning("[AddBook] Request validation failed: {Errors}", string.Join("; ", validationResults.Select(r => r.ErrorMessage)));
                    foreach (var validationResult in validationResults)
                    {
                        ModelState.AddModelError(string.Join(",", validationResult.MemberNames), validationResult.ErrorMessage ?? "Validation error");
                    }
                    return Page();
                }

                _logger.LogDebug("[AddBook] Normalizing request data...");
                var normalized = request.WithNormalizedData();
                
                _logger.LogDebug("[AddBook] Creating or finding author...");
                var authorEntity = await AuthorResolver.GetOrCreateAsync(_context, normalized.Author, normalized.AuthorAvatarUrl, cancellationToken);
                
                _logger.LogDebug("[AddBook] Converting to entity...");
                var book = normalized.ToEntity(authorEntity);
                CreateBookRequest.SyncQuoteSnapshot(book);

                _logger.LogDebug("[AddBook] Adding book to context...");
                _context.Books.Add(book);
                
                _logger.LogDebug("[AddBook] Saving changes...");
                await _context.SaveChangesAsync(cancellationToken);

                timer.Stop();
                _logger.LogInformation("[AddBook] Book created with id {Id} in {Elapsed} ms", book.Id, timer.ElapsedMilliseconds);
                return RedirectToPage("./Index");
            }
            catch (Exception ex)
            {
                timer.Stop();
                _logger.LogError(ex, "[AddBook] Failed to create book '{Title}' by {Author} after {Elapsed} ms", Title, Author, timer.ElapsedMilliseconds);
                
                ModelState.AddModelError(string.Empty, "We could not add this book right now. Please try again.");
                return Page();
            }
        }
    }
}
