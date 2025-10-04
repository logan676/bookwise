using System.Threading;
using System.Threading.Tasks;

namespace BookWise.Web.Services.CommunityContent;

public interface IBookCommunityContentRefresher
{
    Task RefreshAsync(BookCommunityContentWorkItem workItem, CancellationToken cancellationToken);
}
