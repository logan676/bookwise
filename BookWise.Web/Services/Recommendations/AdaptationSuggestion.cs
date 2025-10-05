namespace BookWise.Web.Services.Recommendations;

public sealed record AdaptationSuggestion(
    string Title,
    string Type,
    string? ImageUrl,
    string? Rationale,
    decimal? ConfidenceScore);

