using System.Threading;
using System.Threading.Tasks;

namespace BookWise.Web.Services.CommunityContent;

public interface IBookCommunityContentScheduler
{
    Task ScheduleFetchAsync(int bookId, string? doubanSubjectId, CancellationToken cancellationToken = default);
}
