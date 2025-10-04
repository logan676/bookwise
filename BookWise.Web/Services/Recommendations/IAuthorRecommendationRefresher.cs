namespace BookWise.Web.Services.Recommendations;

public interface IAuthorRecommendationRefresher
{
    Task RefreshAsync(AuthorRecommendationWorkItem workItem, CancellationToken cancellationToken);
}
