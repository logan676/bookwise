namespace BookWise.Web.Services.Recommendations;

public sealed record AuthorSuggestion(
    string Name,
    string? Rationale,
    string? ImageUrl,
    decimal? ConfidenceScore);
