using System.Threading.Channels;

namespace BookWise.Web.Services.Recommendations;

public sealed class AuthorRecommendationQueue
{
    private readonly Channel<AuthorRecommendationWorkItem> _channel;

    public AuthorRecommendationQueue()
    {
        _channel = Channel.CreateUnbounded<AuthorRecommendationWorkItem>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ValueTask QueueAsync(AuthorRecommendationWorkItem workItem, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        return _channel.Writer.WriteAsync(workItem, cancellationToken);
    }

    public IAsyncEnumerable<AuthorRecommendationWorkItem> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}

public sealed record AuthorRecommendationWorkItem(
    bool FullRefresh,
    IReadOnlyCollection<string> FocusAuthors)
{
    public static AuthorRecommendationWorkItem ForAuthors(IEnumerable<string> authors)
    {
        var authorList = authors?.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();
        return new AuthorRecommendationWorkItem(false, authorList);
    }

    public static AuthorRecommendationWorkItem FullRefreshRequest() => new(true, Array.Empty<string>());
}
