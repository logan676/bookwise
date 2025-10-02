using System.ComponentModel.DataAnnotations;

namespace BookWise.Web.Models;

public class Book
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Author { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Url]
    [MaxLength(500)]
    public string? CoverImageUrl { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }

    public bool IsFavorite { get; set; }

    [Range(0, 5)]
    public decimal? Rating { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}
