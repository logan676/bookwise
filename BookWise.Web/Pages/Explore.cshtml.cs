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
            (QuoteOfTheDay, Quotes) = await LoadQuotesAsync(cancellationToken);
            RecommendedAuthors = await LoadRecommendedAuthorsAsync(cancellationToken);
            RecommendedSeries = await LoadRecommendedSeriesAsync(cancellationToken);
            RecommendedAdaptations = await LoadRecommendedAdaptationsAsync(cancellationToken);
        }

        private async Task<IReadOnlyList<AuthorProfile>> LoadAuthorsAsync(CancellationToken cancellationToken)
        {
            // Project only required fields to avoid selecting unmapped columns
            var authors = await _context.Authors
                .AsNoTracking()
                .Where(a => a.Books.Any())
                .Select(a => new
                {
                    a.Name,
                    a.AvatarUrl,
                    Books = a.Books.Select(b => new
                    {
                        b.Title,
                        b.Category,
                        b.Status,
                        b.CoverImageUrl,
                        b.Description
                    }).ToList()
                })
                .OrderBy(a => a.Name)
                .ToListAsync(cancellationToken);

            if (authors.Count == 0)
            {
                return Array.Empty<AuthorProfile>();
            }

            var authorProfiles = authors
                .Select(a =>
                {
                    var works = a.Books
                        .OrderBy(b => b.Title, StringComparer.OrdinalIgnoreCase)
                        .Select(b => new
                        {
                            b.Title,
                            b.Status,
                            Subtitle = BuildBookSubtitle(new Book { Category = b.Category, Status = b.Status }),
                            CoverUrl = string.IsNullOrWhiteSpace(b.CoverImageUrl)
                                ? "/img/book-placeholder.svg"
                                : b.CoverImageUrl,
                            Description = b.Description
                        })
                        .ToList();

                    var libraryWorks = works
                        .Where(w => IsLibraryStatus(w.Status))
                        .Select(w => new AuthorWork
                        {
                            Title = w.Title,
                            Subtitle = w.Subtitle,
                            CoverUrl = w.CoverUrl
                        })
                        .ToList();

                    var availableWorks = works
                        .Where(w => !IsLibraryStatus(w.Status))
                        .Select(w => new AuthorWork
                        {
                            Title = w.Title,
                            Subtitle = w.Subtitle,
                            CoverUrl = w.CoverUrl
                        })
                        .ToList();

                    return new AuthorProfile
                    {
                        Slug = GenerateSlug(a.Name),
                        Name = a.Name,
                        Summary = BuildAuthorSummary(
                            works.Select(w => new Book { Description = w.Description }).ToList()
                        ),
                        PhotoUrl = string.IsNullOrWhiteSpace(a.AvatarUrl) ? "/img/book-placeholder.svg" : a.AvatarUrl!,
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

            // Order by GeneratedAt on the client side to avoid SQLite DateTimeOffset ordering issues
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

        private async Task<IReadOnlyList<RecommendedSeriesItem>> LoadRecommendedSeriesAsync(CancellationToken cancellationToken)
        {
            // For now, return empty list as there's no data source for series recommendations
            await Task.CompletedTask;
            return Array.Empty<RecommendedSeriesItem>();
        }

        private async Task<IReadOnlyList<RecommendedAdaptation>> LoadRecommendedAdaptationsAsync(CancellationToken cancellationToken)
        {
            // For now, return empty list as there's no data source for adaptation recommendations
            await Task.CompletedTask;
            return Array.Empty<RecommendedAdaptation>();
        }

        private async Task<(QuoteCard QuoteOfTheDay, IReadOnlyList<QuoteCard> Quotes)> LoadQuotesAsync(CancellationToken cancellationToken)
        {
            var quoteEntities = await _context.BookQuotes
                .AsNoTracking()
                .Include(q => q.Book)
                .ToListAsync(cancellationToken);

            if (quoteEntities.Count == 0)
            {
                return (QuoteCard.Empty, Array.Empty<QuoteCard>());
            }

            // Order by AddedOn on the client side to avoid SQLite DateTimeOffset ordering issues
            quoteEntities = quoteEntities
                .OrderByDescending(q => q.AddedOn)
                .ToList();

            var cards = quoteEntities
                .Select(q => new QuoteCard
                {
                    Text = q.Text,
                    Author = q.Author,
                    Source = q.Source,
                    BackgroundImageUrl = !string.IsNullOrWhiteSpace(q.BackgroundImageUrl)
                        ? q.BackgroundImageUrl
                        : string.IsNullOrWhiteSpace(q.Book?.CoverImageUrl) ? null : q.Book?.CoverImageUrl
                })
                .ToList();

            var quoteOfTheDay = cards[0];
            var remaining = cards.Skip(1).ToList();

            return (quoteOfTheDay, remaining);
        }

        private RecommendedAuthor MapToRecommendedAuthor(AuthorRecommendation recommendation)
        {
            var description = !string.IsNullOrWhiteSpace(recommendation.Rationale)
                ? recommendation.Rationale
                : $"Readers who enjoy {recommendation.FocusAuthor} also like {recommendation.RecommendedAuthor}.";

            var imageUrl = recommendation.ImageUrl;
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                // Try to use stored avatar for this author if available (only select AvatarUrl)
                var existingAvatarUrl = _context.Authors.AsNoTracking()
                    .Where(a => a.Name == recommendation.RecommendedAuthor)
                    .Select(a => a.AvatarUrl)
                    .FirstOrDefault();
                imageUrl = string.IsNullOrWhiteSpace(existingAvatarUrl)
                    ? "/img/book-placeholder.svg"
                    : existingAvatarUrl!;
            }

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

        // No pravatar fallback; avatars come from Douban when available.

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
