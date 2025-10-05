using BookWise.Web.Data;
using BookWise.Web.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BookWise.Web.Services.Background;

public sealed class AvatarCacheBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<AvatarCacheBackgroundService> _logger;
    
    public AvatarCacheBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<AvatarCacheBackgroundService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit before starting to allow the application to fully start
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CacheAuthorAvatarsAsync(stoppingToken);
                
                // Run every 6 hours
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation, exit gracefully
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in avatar cache background service");
                
                // Wait a bit before retrying on error
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    private async Task CacheAuthorAvatarsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BookWiseContext>();
        var avatarCacheService = scope.ServiceProvider.GetRequiredService<IAvatarCacheService>();

        _logger.LogInformation("Starting avatar cache background task");

        try
        {
            // Get authors with external avatar URLs that may not be cached yet
            var authorsWithExternalAvatars = await context.Authors
                .AsNoTracking()
                .Where(a => a.AvatarUrl != null && a.AvatarUrl.StartsWith("http"))
                .Select(a => new { a.Name, a.AvatarUrl })
                .ToListAsync(cancellationToken);

            var cachedCount = 0;
            var failedCount = 0;

            foreach (var author in authorsWithExternalAvatars)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    // Check if already cached
                    if (await avatarCacheService.IsCachedAsync(author.AvatarUrl!))
                    {
                        continue;
                    }

                    // Attempt to cache the avatar
                    var cachedUrl = await avatarCacheService.GetCachedAvatarUrlAsync(author.AvatarUrl!, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(cachedUrl))
                    {
                        cachedCount++;
                        _logger.LogDebug("Cached avatar for author {AuthorName}", author.Name);
                    }
                    else
                    {
                        failedCount++;
                        _logger.LogWarning("Failed to cache avatar for author {AuthorName} from {AvatarUrl}", author.Name, author.AvatarUrl);
                    }

                    // Small delay to avoid overwhelming external servers
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _logger.LogWarning(ex, "Error caching avatar for author {AuthorName} from {AvatarUrl}", author.Name, author.AvatarUrl);
                }
            }

            _logger.LogInformation("Avatar cache background task completed. Cached: {CachedCount}, Failed: {FailedCount}", cachedCount, failedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in avatar cache background task");
        }
    }
}