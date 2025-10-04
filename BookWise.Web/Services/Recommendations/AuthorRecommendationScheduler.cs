using Microsoft.Extensions.Logging;

namespace BookWise.Web.Services.Recommendations;

public sealed class AuthorRecommendationScheduler : IAuthorRecommendationScheduler
{
    private readonly AuthorRecommendationQueue _queue;
    private readonly ILogger<AuthorRecommendationScheduler> _logger;

    public AuthorRecommendationScheduler(
        AuthorRecommendationQueue queue,
        ILogger<AuthorRecommendationScheduler> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    public async Task ScheduleRefreshForAuthorsAsync(IEnumerable<string> authors, CancellationToken cancellationToken = default)
    {
        var normalized = authors?
            .Where(author => !string.IsNullOrWhiteSpace(author))
            .Select(author => author.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

        if (normalized.Length == 0)
        {
            return;
        }

        await _queue.QueueAsync(AuthorRecommendationWorkItem.ForAuthors(normalized), cancellationToken);
        _logger.LogInformation(
            "Queued author recommendation refresh for {Count} authors", normalized.Length);
    }

    public async Task ScheduleFullRefreshAsync(CancellationToken cancellationToken = default)
    {
        await _queue.QueueAsync(AuthorRecommendationWorkItem.FullRefreshRequest(), cancellationToken);
        _logger.LogInformation("Queued full author recommendation refresh");
    }
}
