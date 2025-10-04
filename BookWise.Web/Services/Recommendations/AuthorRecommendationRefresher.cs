using System.Collections.Generic;
using BookWise.Web.Data;
using BookWise.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookWise.Web.Services.Recommendations;

public sealed class AuthorRecommendationRefresher : IAuthorRecommendationRefresher
{
    private readonly BookWiseContext _dbContext;
    private readonly IDeepSeekRecommendationClient _recommendationClient;
    private readonly ILogger<AuthorRecommendationRefresher> _logger;

    public AuthorRecommendationRefresher(
        BookWiseContext dbContext,
        IDeepSeekRecommendationClient recommendationClient,
        ILogger<AuthorRecommendationRefresher> logger)
    {
        _dbContext = dbContext;
        _recommendationClient = recommendationClient;
        _logger = logger;
    }

    public async Task RefreshAsync(AuthorRecommendationWorkItem workItem, CancellationToken cancellationToken)
    {
        var libraryAuthors = await _dbContext.Authors
            .AsNoTracking()
            .Where(author => author.Books.Any())
            .OrderBy(author => author.Name)
            .Select(author => author.Name)
            .ToListAsync(cancellationToken);

        if (libraryAuthors.Count == 0)
        {
            _logger.LogInformation("Skipped author recommendation refresh because the library is empty.");
            return;
        }

        IReadOnlyList<string> focusAuthors;
        if (workItem.FullRefresh)
        {
            focusAuthors = libraryAuthors;
        }
        else
        {
            var focusSet = new HashSet<string>(workItem.FocusAuthors, StringComparer.OrdinalIgnoreCase);
            focusAuthors = libraryAuthors.Where(focusSet.Contains).ToList();
        }

        if (focusAuthors.Count == 0)
        {
            return;
        }

        foreach (var focusAuthor in focusAuthors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var suggestions = await _recommendationClient.GetRecommendedAuthorsAsync(
                    focusAuthor,
                    libraryAuthors,
                    cancellationToken);

                await PersistSuggestionsAsync(focusAuthor, suggestions, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh author recommendations for '{FocusAuthor}'", focusAuthor);
            }
        }
    }

    private async Task PersistSuggestionsAsync(
        string focusAuthor,
        IReadOnlyList<AuthorSuggestion> suggestions,
        CancellationToken cancellationToken)
    {
        var focus = focusAuthor.Trim();

        var existing = await _dbContext.AuthorRecommendations
            .Where(r => r.FocusAuthor == focus)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            _dbContext.AuthorRecommendations.RemoveRange(existing);
        }

        if (suggestions.Count == 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var suggestion in suggestions)
        {
            var recommended = suggestion.Name?.Trim();
            if (string.IsNullOrWhiteSpace(recommended))
            {
                continue;
            }

            if (!seen.Add(recommended))
            {
                continue;
            }

            var rationale = suggestion.Rationale?.Trim();
            var imageUrl = suggestion.ImageUrl?.Trim();
            var confidence = suggestion.ConfidenceScore;
            if (confidence is > 1)
            {
                confidence = 1;
            }
            else if (confidence is < 0)
            {
                confidence = 0;
            }

            _dbContext.AuthorRecommendations.Add(new AuthorRecommendation
            {
                FocusAuthor = Truncate(focus, 200) ?? focus,
                RecommendedAuthor = Truncate(recommended, 200) ?? recommended,
                Rationale = Truncate(rationale, 1000),
                ImageUrl = Truncate(imageUrl, 500),
                ConfidenceScore = confidence,
                GeneratedAt = DateTimeOffset.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
