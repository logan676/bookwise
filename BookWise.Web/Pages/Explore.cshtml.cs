using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BookWise.Web.Data;
using BookWise.Web.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BookWise.Web.Pages
{
    public class ExploreModel : PageModel
    {
        private readonly BookWiseContext _context;

        public ExploreModel(BookWiseContext context)
        {
            _context = context;
        }

        public IReadOnlyList<AuthorProfile> Authors { get; private set; } = Array.Empty<AuthorProfile>();
        public QuoteCard QuoteOfTheDay { get; private set; } = QuoteCard.Empty;
        public IReadOnlyList<QuoteCard> Quotes { get; private set; } = Array.Empty<QuoteCard>();
        public IReadOnlyList<RecommendedAuthor> RecommendedAuthors { get; private set; } = Array.Empty<RecommendedAuthor>();
        public IReadOnlyList<RecommendedSeriesItem> RecommendedSeries { get; private set; } = Array.Empty<RecommendedSeriesItem>();
        public IReadOnlyList<RecommendedAdaptation> RecommendedAdaptations { get; private set; } = Array.Empty<RecommendedAdaptation>();

        public async Task OnGetAsync()
        {
            ViewData["Title"] = "Explore";
            var cancellationToken = HttpContext.RequestAborted;
            Authors = await LoadAuthorsAsync(cancellationToken);
            (QuoteOfTheDay, Quotes) = BuildQuotes();

            var (fallbackAuthors, series, adaptations) = BuildRecommendations();
            RecommendedSeries = series;
            RecommendedAdaptations = adaptations;

            var dynamicAuthors = await LoadRecommendedAuthorsAsync(cancellationToken);
            RecommendedAuthors = dynamicAuthors.Count > 0 ? dynamicAuthors : fallbackAuthors;
        }

        private async Task<IReadOnlyList<AuthorProfile>> LoadAuthorsAsync(CancellationToken cancellationToken)
        {
            var books = await _context.Books
                .AsNoTracking()
                .OrderBy(book => book.Author)
                .ThenBy(book => book.Title)
                .ToListAsync(cancellationToken);

            if (books.Count == 0)
            {
                return Array.Empty<AuthorProfile>();
            }

            var authorProfiles = books
                .GroupBy(book => book.Author)
                .Select(group =>
                {
                    var works = group.ToList();
                    var libraryWorks = works
                        .Where(book => IsLibraryStatus(book.Status))
                        .OrderBy(book => book.Title, StringComparer.OrdinalIgnoreCase)
                        .Select(MapToAuthorWork)
                        .ToList();

                    var availableWorks = works
                        .Where(book => !IsLibraryStatus(book.Status))
                        .OrderBy(book => book.Title, StringComparer.OrdinalIgnoreCase)
                        .Select(MapToAuthorWork)
                        .ToList();

                    return new AuthorProfile
                    {
                        Slug = GenerateSlug(group.Key),
                        Name = group.Key,
                        Summary = BuildAuthorSummary(works),
                        PhotoUrl = BuildAuthorAvatarUrl(group.Key),
                        WorkCount = works.Count,
                        Library = libraryWorks,
                        AvailableWorks = availableWorks
                    };
                })
                .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return authorProfiles;
        }

        private async Task<IReadOnlyList<RecommendedAuthor>> LoadRecommendedAuthorsAsync(CancellationToken cancellationToken)
        {
            var recommendations = await _context.AuthorRecommendations
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (recommendations.Count == 0)
            {
                return Array.Empty<RecommendedAuthor>();
            }

            var prioritized = recommendations
                .OrderByDescending(r => r.ConfidenceScore ?? 0m)
                .ThenByDescending(r => r.GeneratedAt)
                .Take(24)
                .ToList();

            var grouped = prioritized
                .GroupBy(r => r.RecommendedAuthor, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(r => r.ConfidenceScore ?? 0m)
                    .ThenByDescending(r => r.GeneratedAt)
                    .First())
                .OrderByDescending(r => r.ConfidenceScore ?? 0m)
                .ThenByDescending(r => r.GeneratedAt)
                .Take(6)
                .Select(MapToRecommendedAuthor)
                .ToList();

            return grouped;
        }

        private RecommendedAuthor MapToRecommendedAuthor(AuthorRecommendation recommendation)
        {
            var description = !string.IsNullOrWhiteSpace(recommendation.Rationale)
                ? recommendation.Rationale
                : $"Readers who enjoy {recommendation.FocusAuthor} also like {recommendation.RecommendedAuthor}.";

            var imageUrl = !string.IsNullOrWhiteSpace(recommendation.ImageUrl)
                ? recommendation.ImageUrl!
                : BuildAuthorAvatarUrl(recommendation.RecommendedAuthor);

            return new RecommendedAuthor
            {
                Name = recommendation.RecommendedAuthor,
                Description = description,
                ImageUrl = imageUrl
            };
        }

        private static AuthorWork MapToAuthorWork(Book book)
        {
            return new AuthorWork
            {
                Title = book.Title,
                Subtitle = BuildBookSubtitle(book),
                CoverUrl = string.IsNullOrWhiteSpace(book.CoverImageUrl)
                    ? "/img/book-placeholder.svg"
                    : book.CoverImageUrl
            };
        }

        private static string BuildAuthorSummary(IReadOnlyCollection<Book> books)
        {
            var summarySource = books
                .Select(book => book.Description)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            if (!string.IsNullOrWhiteSpace(summarySource))
            {
                return TrimToLength(summarySource, 160);
            }

            var count = books.Count;
            return count switch
            {
                0 => "No books yet for this author.",
                1 => "You have 1 book from this author on your shelf.",
                _ => $"You have {count} books from this author on your shelf."
            };
        }

        private static string TrimToLength(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value[..maxLength].TrimEnd() + "...";
        }

        private static string BuildAuthorAvatarUrl(string author)
        {
            if (string.IsNullOrWhiteSpace(author))
            {
                return "https://i.pravatar.cc/96?img=1";
            }

            var identifier = Uri.EscapeDataString(author.Trim());
            return $"https://i.pravatar.cc/96?u={identifier}";
        }

        private static bool IsLibraryStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return status.Trim().ToLowerInvariant() is "reading" or "read";
        }

        private static string BuildBookSubtitle(Book book)
        {
            if (!string.IsNullOrWhiteSpace(book.Category))
            {
                return book.Category;
            }

            return NormalizeStatusLabel(book.Status);
        }

        private static string NormalizeStatusLabel(string? status)
        {
            return status?.Trim().ToLowerInvariant() switch
            {
                "reading" => "In progress",
                "read" => "Completed",
                "plan-to-read" => "On deck",
                null or "" => "Uncategorized",
                _ => status!
            };
        }

        private static string GenerateSlug(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "author";
            }

            var lower = value.Trim().ToLowerInvariant();
            var builder = new StringBuilder(lower.Length);
            var previousWasHyphen = false;

            foreach (var character in lower)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                    previousWasHyphen = false;
                }
                else if (char.IsWhiteSpace(character) || character is '-' or '_')
                {
                    if (!previousWasHyphen && builder.Length > 0)
                    {
                        builder.Append('-');
                        previousWasHyphen = true;
                    }
                }
            }

            var slug = builder.ToString().Trim('-');
            return string.IsNullOrWhiteSpace(slug) ? "author" : slug;
        }

        private static (QuoteCard QuoteOfTheDay, IReadOnlyList<QuoteCard> Quotes) BuildQuotes()
        {
            var quoteOfTheDay = new QuoteCard
            {
                Text = "The only way to do great work is to love what you do.",
                Author = "Steve Jobs",
                Source = "From 'The Innovator's Dilemma' by Clayton Christensen",
                BackgroundImageUrl = "https://images.unsplash.com/photo-1520607162513-77705c0f0d4a?auto=format&fit=crop&w=1600&q=80"
            };

            var quotes = new List<QuoteCard>
            {
                new()
                {
                    Text = "The greatest glory in living lies not in never falling, but in rising every time we fall.",
                    Author = "Nelson Mandela",
                    Source = "Long Walk to Freedom"
                },
                new()
                {
                    Text = "The way to get started is to quit talking and begin doing.",
                    Author = "Walt Disney"
                },
                new()
                {
                    Text = "Your time is limited, so don't waste it living someone else's life.",
                    Author = "Steve Jobs",
                    Source = "Stanford Commencement"
                },
                new()
                {
                    Text = "It always seems impossible until it's done.",
                    Author = "Nelson Mandela"
                },
                new()
                {
                    Text = "Imagination is more important than knowledge.",
                    Author = "Albert Einstein"
                }
            };

            return (quoteOfTheDay, quotes);
        }

        private static (IReadOnlyList<RecommendedAuthor> Authors, IReadOnlyList<RecommendedSeriesItem> Series, IReadOnlyList<RecommendedAdaptation> Adaptations) BuildRecommendations()
        {
            var authors = new List<RecommendedAuthor>
            {
                new()
                {
                    Name = "Jane Doe",
                    Description = "Master of historical fiction with a modern twist.",
                    ImageUrl = "https://i.pravatar.cc/150?img=1"
                },
                new()
                {
                    Name = "John Smith",
                    Description = "Award-winning author of gripping sci-fi thrillers.",
                    ImageUrl = "https://i.pravatar.cc/150?img=2"
                },
                new()
                {
                    Name = "Emily White",
                    Description = "Bestselling writer of cozy mysteries and heartwarming tales.",
                    ImageUrl = "https://i.pravatar.cc/150?img=3"
                }
            };

            var series = new List<RecommendedSeriesItem>
            {
                new()
                {
                    Title = "The Sunstone Saga",
                    Installment = "Book 1",
                    CoverUrl = "https://covers.openlibrary.org/b/id/8225261-L.jpg"
                },
                new()
                {
                    Title = "The Sunstone Saga",
                    Installment = "Book 2",
                    CoverUrl = "https://covers.openlibrary.org/b/id/12593687-L.jpg"
                },
                new()
                {
                    Title = "The Starfall Chronicles",
                    Installment = "Book 1",
                    CoverUrl = "https://covers.openlibrary.org/b/id/8091016-L.jpg"
                },
                new()
                {
                    Title = "The Starfall Chronicles",
                    Installment = "Book 2",
                    CoverUrl = "https://covers.openlibrary.org/b/id/8091017-L.jpg"
                }
            };

            var adaptations = new List<RecommendedAdaptation>
            {
                new()
                {
                    Title = "Movie 1",
                    Type = "Movie",
                    ImageUrl = "https://picsum.photos/300/400?random=1"
                },
                new()
                {
                    Title = "TV Show 1",
                    Type = "TV Show",
                    ImageUrl = "https://picsum.photos/300/400?random=2"
                },
                new()
                {
                    Title = "Movie 2",
                    Type = "Movie",
                    ImageUrl = "https://picsum.photos/300/400?random=3"
                },
                new()
                {
                    Title = "TV Show 2",
                    Type = "TV Show",
                    ImageUrl = "https://picsum.photos/300/400?random=4"
                },
                new()
                {
                    Title = "Movie 3",
                    Type = "Movie",
                    ImageUrl = "https://picsum.photos/300/400?random=5"
                }
            };

            return (authors, series, adaptations);
        }


        public class AuthorProfile
        {
            public string Slug { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string Summary { get; init; } = string.Empty;
            public string PhotoUrl { get; init; } = string.Empty;
            public int WorkCount { get; init; }
            public IReadOnlyList<AuthorWork> Library { get; init; } = Array.Empty<AuthorWork>();
            public IReadOnlyList<AuthorWork> AvailableWorks { get; init; } = Array.Empty<AuthorWork>();
        }

        public class AuthorWork
        {
            public string Title { get; init; } = string.Empty;
            public string Subtitle { get; init; } = string.Empty;
            public string CoverUrl { get; init; } = string.Empty;
        }

        public class QuoteCard
        {
            public static QuoteCard Empty { get; } = new();

            public string Text { get; init; } = string.Empty;
            public string Author { get; init; } = string.Empty;
            public string? Source { get; init; }
            public string? BackgroundImageUrl { get; init; }
        }

        public class RecommendedAuthor
        {
            public string Name { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public string ImageUrl { get; init; } = string.Empty;
        }

        public class RecommendedSeriesItem
        {
            public string Title { get; init; } = string.Empty;
            public string Installment { get; init; } = string.Empty;
            public string CoverUrl { get; init; } = string.Empty;
        }

        public class RecommendedAdaptation
        {
            public string Title { get; init; } = string.Empty;
            public string Type { get; init; } = string.Empty;
            public string ImageUrl { get; init; } = string.Empty;
        }
    }
}
