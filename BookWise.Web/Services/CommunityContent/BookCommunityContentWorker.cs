using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookWise.Web.Services.CommunityContent;

public sealed class BookCommunityContentWorker : BackgroundService
{
    private readonly BookCommunityContentQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookCommunityContentWorker> _logger;

    public BookCommunityContentWorker(
        BookCommunityContentQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<BookCommunityContentWorker> logger)
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
                var refresher = scope.ServiceProvider.GetRequiredService<IBookCommunityContentRefresher>();
                await refresher.RefreshAsync(workItem, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh community content for book {BookId}", workItem.BookId);
            }
        }
    }
}
