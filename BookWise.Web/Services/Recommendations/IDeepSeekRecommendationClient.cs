namespace BookWise.Web.Services.Recommendations;

public interface IDeepSeekRecommendationClient
{
    Task<IReadOnlyList<AuthorSuggestion>> GetRecommendedAuthorsAsync(
        string focusAuthor,
        IReadOnlyCollection<string> libraryAuthors,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SeriesSuggestion>> GetRecommendedSeriesAsync(
        IReadOnlyCollection<string> libraryTitles,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AdaptationSuggestion>> GetRecommendedAdaptationsAsync(
        IReadOnlyCollection<string> libraryTitles,
        CancellationToken cancellationToken);
}
