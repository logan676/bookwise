using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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

    public int AuthorId { get; set; }

    [ForeignKey(nameof(AuthorId))]
    public Author? AuthorDetails { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Url]
    [MaxLength(500)]
    public string? CoverImageUrl { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }

    [MaxLength(200)]
    public string? Publisher { get; set; }

    [MaxLength(500)]
    public string? Quote { get; set; }

    [MaxLength(20)]
    public string? ISBN { get; set; }

    [MaxLength(32)]
    public string? DoubanSubjectId { get; set; }

    public string Status { get; set; } = "plan-to-read"; // plan-to-read, reading, read

    public bool IsFavorite { get; set; }

    [Range(0, 5)]
    [Column("Rating")]
    public decimal? PersonalRating { get; set; }

    [Range(0, 5)]
    public decimal? PublicRating { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<BookRemark> Remarks { get; set; } = new List<BookRemark>();

    public ICollection<BookQuote> Quotes { get; set; } = new List<BookQuote>();
}
