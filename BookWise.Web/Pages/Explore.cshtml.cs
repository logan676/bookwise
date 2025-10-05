using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BookWise.Web.Data;
using BookWise.Web.Models;
using BookWise.Web.Services.Caching;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BookWise.Web.Pages
{
    public class ExploreModel : PageModel
    {
        private readonly BookWiseContext _context;
        private readonly IAvatarCacheService _avatarCacheService;
        private readonly BookWise.Web.Services.Recommendations.IDeepSeekRecommendationClient _deepSeek;

        // Explore page tuning knobs
        private const int MaxRecentQuotes = 200;
        private const int MaxQuoteGroups = 8;
        private const int MaxQuotesPerGroup = 4;

        public ExploreModel(BookWiseContext context, IAvatarCacheService avatarCacheService, BookWise.Web.Services.Recommendations.IDeepSeekRecommendationClient deepSeek)
        {
            _context = context;
            _avatarCacheService = avatarCacheService;
            _deepSeek = deepSeek;
        }

        public IReadOnlyList<AuthorProfile> Authors { get; private set; } = Array.Empty<AuthorProfile>();
        public QuoteCard QuoteOfTheDay { get; private set; } = QuoteCard.Empty;
        public IReadOnlyList<QuoteGroup> GroupedQuotes { get; private set; } = Array.Empty<QuoteGroup>();
        public IReadOnlyList<RecommendedAuthor> RecommendedAuthors { get; private set; } = Array.Empty<RecommendedAuthor>();
        public IReadOnlyList<RecommendedSeriesItem> RecommendedSeries { get; private set; } = Array.Empty<RecommendedSeriesItem>();
        public IReadOnlyList<RecommendedAdaptation> RecommendedAdaptations { get; private set; } = Array.Empty<RecommendedAdaptation>();

        public async Task OnGetAsync()
        {
            ViewData["Title"] = "Explore";
            var cancellationToken = HttpContext.RequestAborted;
            Authors = await LoadAuthorsAsync(cancellationToken);
            (QuoteOfTheDay, GroupedQuotes) = await LoadQuotesAsync(cancellationToken);
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
                    a.ProfileSummary,
                    a.ProfileNotableWorks,
                    a.ProfileGender,
                    a.ProfileBirthDate,
                    a.ProfileBirthPlace,
                    a.ProfileOccupation,
                    a.ProfileOtherNames,
                    a.ProfileWebsiteUrl,
                    a.DoubanProfileUrl,
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

            var authorProfiles = new List<AuthorProfile>();
            
            foreach (var a in authors)
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

                // Prefer stored profile summary when present; do NOT fall back to book descriptions
                var summary = !string.IsNullOrWhiteSpace(a.ProfileSummary)
                    ? TrimToLength(a.ProfileSummary!, 200)
                    : BuildAuthorSummary(
                        works.Select(w => new Book { Description = null }).ToList()
                      );

                // Get cached avatar URL or fallback to placeholder
                var cachedAvatarUrl = await GetCachedAvatarUrlAsync(a.AvatarUrl, cancellationToken);

                authorProfiles.Add(new AuthorProfile
                {
                    Slug = GenerateSlug(a.Name),
                    Name = a.Name,
                    Summary = summary,
                    PhotoUrl = cachedAvatarUrl ?? "/img/author-placeholder.svg",
                    WorkCount = works.Count,
                    Library = libraryWorks,
                    AvailableWorks = availableWorks,
                    Gender = EmptyToNull(a.ProfileGender),
                    BirthDate = EmptyToNull(a.ProfileBirthDate),
                    BirthPlace = EmptyToNull(a.ProfileBirthPlace),
                    Occupation = EmptyToNull(a.ProfileOccupation),
                    WebsiteUrl = EmptyToNull(a.ProfileWebsiteUrl),
                    DoubanProfileUrl = EmptyToNull(a.DoubanProfileUrl),
                    OtherNames = SplitToList(a.ProfileOtherNames),
                    NotableWorks = SplitToList(a.ProfileNotableWorks)
                });
            }

            return authorProfiles.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private async Task<string?> GetCachedAvatarUrlAsync(string? originalUrl, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(originalUrl))
            {
                return null;
            }

            try
            {
                // For external URLs (like Douban), use the cache service
                if (originalUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    // Important: do NOT trigger a network fetch during initial page render.
                    // Only return a cached URL if available; otherwise fall back to placeholder.
                    return await _avatarCacheService.TryGetCachedAvatarUrlAsync(originalUrl);
                }

                // For local URLs, return as-is
                return originalUrl;
            }
            catch (Exception)
            {
                // If caching fails, return null to fall back to placeholder
                return null;
            }
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
            var titles = await _context.Books
                .AsNoTracking()
                .Where(b => b.Status == "reading" || b.Status == "read")
                .OrderBy(b => b.Title)
                .Select(b => b.Title)
                .ToListAsync(cancellationToken);

            if (titles.Count == 0)
            {
                return Array.Empty<RecommendedSeriesItem>();
            }

            try
            {
                var suggestions = await _deepSeek.GetRecommendedSeriesAsync(titles, cancellationToken);
                if (suggestions.Count == 0)
                {
                    return Array.Empty<RecommendedSeriesItem>();
                }

                return suggestions
                    .Select(s => new RecommendedSeriesItem
                    {
                        Title = s.Title,
                        Installment = s.Installment,
                        CoverUrl = string.IsNullOrWhiteSpace(s.CoverUrl) ? "/img/book-placeholder.svg" : s.CoverUrl!
                    })
                    .ToList();
            }
            catch
            {
                return Array.Empty<RecommendedSeriesItem>();
            }
        }

        private async Task<IReadOnlyList<RecommendedAdaptation>> LoadRecommendedAdaptationsAsync(CancellationToken cancellationToken)
        {
            var titles = await _context.Books
                .AsNoTracking()
                .Where(b => b.Status == "reading" || b.Status == "read")
                .OrderBy(b => b.Title)
                .Select(b => b.Title)
                .ToListAsync(cancellationToken);

            if (titles.Count == 0)
            {
                return Array.Empty<RecommendedAdaptation>();
            }

            try
            {
                var suggestions = await _deepSeek.GetRecommendedAdaptationsAsync(titles, cancellationToken);
                if (suggestions.Count == 0)
                {
                    return Array.Empty<RecommendedAdaptation>();
                }

                return suggestions
                    .Select(s => new RecommendedAdaptation
                    {
                        Title = s.Title,
                        Type = s.Type,
                        ImageUrl = string.IsNullOrWhiteSpace(s.ImageUrl) ? "/img/book-placeholder.svg" : s.ImageUrl!
                    })
                    .ToList();
            }
            catch
            {
                return Array.Empty<RecommendedAdaptation>();
            }
        }

        private async Task<(QuoteCard QuoteOfTheDay, IReadOnlyList<QuoteGroup> Groups)> LoadQuotesAsync(CancellationToken cancellationToken)
        {
            // Load a bounded set of the most recent quotes, projecting only required fields
            var quotes = await _context.BookQuotes
                .AsNoTracking()
                .OrderByDescending(q => q.AddedOn)
                .Take(MaxRecentQuotes)
                .Select(q => new
                {
                    q.Text,
                    q.Author,
                    q.Source,
                    q.BackgroundImageUrl,
                    q.AddedOn,
                    q.BookId,
                    BookTitle = q.Book != null ? q.Book.Title : null,
                    BookAuthor = q.Book != null ? q.Book.Author : null,
                    BookCover = q.Book != null ? q.Book.CoverImageUrl : null
                })
                .ToListAsync(cancellationToken);

            if (quotes.Count == 0)
            {
                return (QuoteCard.Empty, Array.Empty<QuoteGroup>());
            }

            // Quote of the day is the most recent one
            var first = quotes[0];
            var quoteOfTheDay = new QuoteCard
            {
                Text = first.Text,
                Author = first.Author,
                Source = first.Source,
                BackgroundImageUrl = !string.IsNullOrWhiteSpace(first.BackgroundImageUrl)
                    ? first.BackgroundImageUrl
                    : string.IsNullOrWhiteSpace(first.BookCover) ? null : first.BookCover
            };

            // Group the rest by book for a clearer browsing experience
            var groups = quotes
                .Skip(1)
                .GroupBy(q => new
                {
                    q.BookId,
                    Title = string.IsNullOrWhiteSpace(q.BookTitle) ? "Unknown Book" : q.BookTitle,
                    Author = string.IsNullOrWhiteSpace(q.BookAuthor) ? "Unknown Author" : q.BookAuthor,
                    Cover = string.IsNullOrWhiteSpace(q.BookCover) ? "/img/book-placeholder.svg" : q.BookCover!
                })
                .OrderByDescending(g => g.Max(q => q.AddedOn))
                .Take(MaxQuoteGroups)
                .Select(g => new QuoteGroup
                {
                    BookTitle = g.Key.Title,
                    BookAuthor = g.Key.Author,
                    CoverImageUrl = g.Key.Cover!,
                    Quotes = g
                        .OrderByDescending(q => q.AddedOn)
                        .Take(MaxQuotesPerGroup)
                        .Select(q => new QuoteCard
                        {
                            Text = q.Text,
                            Author = q.Author,
                            Source = q.Source,
                            BackgroundImageUrl = !string.IsNullOrWhiteSpace(q.BackgroundImageUrl)
                                ? q.BackgroundImageUrl
                                : string.IsNullOrWhiteSpace(q.BookCover) ? null : q.BookCover
                        })
                        .ToList()
                })
                .ToList();

            return (quoteOfTheDay, groups);
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
            // Only use the author's own introduction (ProfileSummary) for summaries.
            // If unavailable, provide a neutral placeholder rather than a book description.
            var count = books.Count;
            return count switch
            {
                0 => "No introduction available yet.",
                1 => "No introduction available yet.",
                _ => "No introduction available yet."
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

        private static string? EmptyToNull(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static IReadOnlyList<string> SplitToList(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            // Support common separators: comma, Chinese comma、顿号, slash, pipe
            var parts = value
                .Split(new[] { ',', '，', '、', '/', '|', ';' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return parts;
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

            // Extended author profile fields
            public string? Gender { get; init; }
            public string? BirthDate { get; init; }
            public string? BirthPlace { get; init; }
            public string? Occupation { get; init; }
            public IReadOnlyList<string> OtherNames { get; init; } = Array.Empty<string>();
            public IReadOnlyList<string> NotableWorks { get; init; } = Array.Empty<string>();
            public string? WebsiteUrl { get; init; }
            public string? DoubanProfileUrl { get; init; }
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

        public class QuoteGroup
        {
            public string BookTitle { get; init; } = string.Empty;
            public string BookAuthor { get; init; } = string.Empty;
            public string CoverImageUrl { get; init; } = "/img/book-placeholder.svg";
            public IReadOnlyList<QuoteCard> Quotes { get; init; } = Array.Empty<QuoteCard>();
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
