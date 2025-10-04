using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BookWise.Web.Services.CommunityContent;

public sealed class BookCommunityContentScheduler : IBookCommunityContentScheduler
{
    private readonly BookCommunityContentQueue _queue;
    private readonly ILogger<BookCommunityContentScheduler> _logger;

    public BookCommunityContentScheduler(BookCommunityContentQueue queue, ILogger<BookCommunityContentScheduler> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    public async Task ScheduleFetchAsync(int bookId, string? doubanSubjectId, CancellationToken cancellationToken = default)
    {
        if (bookId <= 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(doubanSubjectId))
        {
            _logger.LogDebug("Skipping community content fetch for book {BookId} because Douban subject id is missing.", bookId);
            return;
        }

        var trimmed = doubanSubjectId.Trim();
        var workItem = new BookCommunityContentWorkItem(bookId, trimmed);
        await _queue.QueueAsync(workItem, cancellationToken);
        _logger.LogInformation("Queued community content refresh for book {BookId} (Douban {DoubanSubjectId}).", bookId, trimmed);
    }
}
