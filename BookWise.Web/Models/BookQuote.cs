using System.ComponentModel.DataAnnotations;

namespace BookWise.Web.Models;

public class BookQuote
{
    public int Id { get; set; }

    [Required]
    public int BookId { get; set; }

    public Book Book { get; set; } = null!;

    [Required]
    [MaxLength(500)]
    public string Text { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Author { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Source { get; set; }

    [MaxLength(500)]
    public string? BackgroundImageUrl { get; set; }

    public DateTimeOffset AddedOn { get; set; } = DateTimeOffset.UtcNow;
}
