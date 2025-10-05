using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BookWise.Web.Models;

public class Author
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string NormalizedName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? AvatarUrl { get; set; }

    [MaxLength(20)]
    public string? AvatarStatus { get; set; } // Verified | Failed

    [MaxLength(50)]
    public string? AvatarSource { get; set; } // douban-personage | douban-author | manual

    [MaxLength(2000)]
    public string? ProfileSummary { get; set; }

    [MaxLength(1000)]
    public string? ProfileNotableWorks { get; set; }

    [MaxLength(20)]
    public string? ProfileGender { get; set; }

    [MaxLength(50)]
    public string? ProfileBirthDate { get; set; }

    [MaxLength(200)]
    public string? ProfileBirthPlace { get; set; }

    [MaxLength(200)]
    public string? ProfileOccupation { get; set; }

    [MaxLength(200)]
    public string? ProfileOtherNames { get; set; }

    [MaxLength(500)]
    [Url]
    public string? ProfileWebsiteUrl { get; set; }

    [MaxLength(32)]
    public string? DoubanAuthorId { get; set; }

    [MaxLength(20)]
    public string? DoubanAuthorType { get; set; } // personage | author

    [MaxLength(500)]
    [Url]
    public string? DoubanProfileUrl { get; set; }

    public DateTimeOffset? ProfileRefreshedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<Book> Books { get; set; } = new List<Book>();
}
