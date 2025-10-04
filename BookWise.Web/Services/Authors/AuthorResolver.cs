using System;
using System.Threading;
using System.Threading.Tasks;
using BookWise.Web.Data;
using BookWise.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace BookWise.Web.Services.Authors;

public static class AuthorResolver
{
    public static async Task<Author> GetOrCreateAsync(
        BookWiseContext context,
        string authorName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var displayName = NormalizeDisplayName(authorName);
        if (string.IsNullOrEmpty(displayName))
        {
            throw new ArgumentException("Author name must not be empty.", nameof(authorName));
        }

        var normalizedName = BuildNormalizedKey(displayName);

        var existing = await context.Authors
            .FirstOrDefaultAsync(author => author.NormalizedName == normalizedName, cancellationToken);

        if (existing is not null)
        {
            if (!string.Equals(existing.Name, displayName, StringComparison.Ordinal))
            {
                existing.Name = displayName;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }

            return existing;
        }

        var author = new Author
        {
            Name = displayName,
            NormalizedName = normalizedName,
            CreatedAt = DateTimeOffset.UtcNow
        };

        context.Authors.Add(author);
        return author;
    }

    public static string BuildNormalizedKey(string value)
    {
        var collapsed = NormalizeDisplayName(value);
        return collapsed.ToLowerInvariant();
    }

    private static string NormalizeDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 200)
        {
            trimmed = trimmed[..200];
        }

        return trimmed;
    }
}
