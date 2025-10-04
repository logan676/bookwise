using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookWise.Web.Services.Recommendations;

public sealed class AuthorRecommendationWorker : BackgroundService
{
    private readonly AuthorRecommendationQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuthorRecommendationWorker> _logger;

    public AuthorRecommendationWorker(
        AuthorRecommendationQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<AuthorRecommendationWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var refresher = scope.ServiceProvider.GetRequiredService<IAuthorRecommendationRefresher>();
                await refresher.RefreshAsync(workItem, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown requested; stop gracefully.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Author recommendation refresh failed");
            }
        }
    }
}
