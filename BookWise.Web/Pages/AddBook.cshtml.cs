using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using BookWise.Web.Models;
using BookWise.Web.Data;

namespace BookWise.Web.Pages
{
    public class AddBookModel : PageModel
    {
        private readonly BookWiseContext _context;

        public AddBookModel(BookWiseContext context)
        {
            _context = context;
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
        public string? Description { get; set; }

        [BindProperty]
        public string? CoverImageUrl { get; set; }

        [BindProperty]
        public decimal? Rating { get; set; }

        [BindProperty]
        public string Status { get; set; } = "plan-to-read";

        [BindProperty]
        public bool IsFavorite { get; set; }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var book = new Book
            {
                Title = Title,
                Author = Author,
                ISBN = ISBN,
                Category = Category,
                Description = Description,
                CoverImageUrl = CoverImageUrl,
                Rating = Rating,
                Status = Status,
                IsFavorite = IsFavorite
            };

            _context.Books.Add(book);
            await _context.SaveChangesAsync();

            return RedirectToPage("./Index");
        }
    }
}