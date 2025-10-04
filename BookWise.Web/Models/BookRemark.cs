using System.ComponentModel.DataAnnotations;

namespace BookWise.Web.Models;

public enum BookRemarkType
{
    Mine,
    Community
}

public class BookRemark
{
    public int Id { get; set; }

    [Required]
    public int BookId { get; set; }

    public Book Book { get; set; } = null!;

    [MaxLength(200)]
    public string? Title { get; set; }

    [Required]
    [MaxLength(4000)]
    public string Content { get; set; } = string.Empty;

    public DateTimeOffset AddedOn { get; set; } = DateTimeOffset.UtcNow;

    [Required]
    public BookRemarkType Type { get; set; } = BookRemarkType.Mine;
}
