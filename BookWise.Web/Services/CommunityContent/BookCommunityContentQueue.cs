using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace BookWise.Web.Services.CommunityContent;

public sealed class BookCommunityContentQueue
{
    private readonly Channel<BookCommunityContentWorkItem> _channel;

    public BookCommunityContentQueue()
    {
        _channel = Channel.CreateUnbounded<BookCommunityContentWorkItem>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ValueTask QueueAsync(BookCommunityContentWorkItem workItem, CancellationToken cancellationToken)
    {
        return _channel.Writer.WriteAsync(workItem, cancellationToken);
    }

    public IAsyncEnumerable<BookCommunityContentWorkItem> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}

public sealed record BookCommunityContentWorkItem(int BookId, string DoubanSubjectId);
