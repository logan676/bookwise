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

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<Book> Books { get; set; } = new List<Book>();
}
