namespace BookWise.Web.Services.Recommendations;

public interface IAuthorRecommendationScheduler
{
    Task ScheduleRefreshForAuthorsAsync(IEnumerable<string> authors, CancellationToken cancellationToken = default);

    Task ScheduleFullRefreshAsync(CancellationToken cancellationToken = default);
}
