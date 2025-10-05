namespace BookWise.Web.Services.Recommendations;

public sealed record SeriesSuggestion(
    string Title,
    string Installment,
    string? CoverUrl,
    string? Rationale,
    decimal? ConfidenceScore);

