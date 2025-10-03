using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using BookWise.Web.Data;
using BookWise.Web.Models;

namespace BookWise.Web.Pages
{
    public class StatisticsModel : PageModel
    {
        private readonly BookWiseContext _context;

        public StatisticsModel(BookWiseContext context)
        {
            _context = context;
        }

        public int TotalBooks { get; set; }
        public int InReadingBooks { get; set; }
        public int AlreadyReadBooks { get; set; }
        public int PlanToReadBooks { get; set; }
        public List<GenreStatistic> GenreStats { get; set; } = new List<GenreStatistic>();

        public async Task OnGetAsync()
        {
            ViewData["Title"] = "Book Statistics";

            var books = await _context.Books.ToListAsync();
            
            TotalBooks = books.Count;
            InReadingBooks = books.Count(b => b.Status == "in-reading");
            AlreadyReadBooks = books.Count(b => b.Status == "already-read");
            PlanToReadBooks = books.Count(b => b.Status == "plan-to-read");

            // Calculate genre statistics
            GenreStats = books
                .Where(b => !string.IsNullOrEmpty(b.Category))
                .GroupBy(b => b.Category)
                .Select(g => new GenreStatistic 
                { 
                    Genre = g.Key!, 
                    Count = g.Count(),
                    Percentage = Math.Round((double)g.Count() / TotalBooks * 100, 1)
                })
                .OrderByDescending(g => g.Count)
                .ToList();
        }
    }

    public class GenreStatistic
    {
        public string Genre { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }
    }
}