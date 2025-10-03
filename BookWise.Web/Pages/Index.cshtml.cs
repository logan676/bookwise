using BookWise.Web.Data;
using BookWise.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BookWise.Web.Pages;

public class IndexModel : PageModel
{
    private readonly BookWiseContext _context;

    public IndexModel(BookWiseContext context)
    {
        _context = context;
    }

    public List<Book> CurrentlyReading { get; set; } = new();
    public List<Book> AlreadyRead { get; set; } = new();
    public List<Book> PlanToRead { get; set; } = new();
    public bool HasAnyBooks { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var allBooks = await _context.Books.ToListAsync();
        
        // Check if user has any books
        HasAnyBooks = allBooks.Any();
        
        // If no books exist, redirect to Add Book page
        if (!HasAnyBooks)
        {
            return RedirectToPage("/AddBook");
        }

        // Group books by status
        CurrentlyReading = allBooks.Where(b => b.Status == "reading").ToList();
        AlreadyRead = allBooks.Where(b => b.Status == "read").ToList();
        PlanToRead = allBooks.Where(b => b.Status == "plan-to-read").ToList();

        return Page();
    }
}
