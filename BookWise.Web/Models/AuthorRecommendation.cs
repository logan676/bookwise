using System.ComponentModel.DataAnnotations;

namespace BookWise.Web.Models;

public class AuthorRecommendation
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string FocusAuthor { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string RecommendedAuthor { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Rationale { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    public decimal? ConfidenceScore { get; set; }

    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}
