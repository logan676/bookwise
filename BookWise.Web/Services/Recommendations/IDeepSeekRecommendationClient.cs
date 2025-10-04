namespace BookWise.Web.Services.Recommendations;

public interface IDeepSeekRecommendationClient
{
    Task<IReadOnlyList<AuthorSuggestion>> GetRecommendedAuthorsAsync(
        string focusAuthor,
        IReadOnlyCollection<string> libraryAuthors,
        CancellationToken cancellationToken);
}
