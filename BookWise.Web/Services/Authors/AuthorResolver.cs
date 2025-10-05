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
        string? avatarUrl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var displayName = NormalizeDisplayName(authorName);
        if (string.IsNullOrEmpty(displayName))
        {
            throw new ArgumentException("Author name must not be empty.", nameof(authorName));
        }

        var normalizedName = BuildNormalizedKey(displayName);
        var normalizedAvatar = NormalizeAvatarUrl(avatarUrl);

        var existing = await context.Authors
            .FirstOrDefaultAsync(author => author.NormalizedName == normalizedName, cancellationToken);

        if (existing is not null)
        {
            var hasChanges = false;
            if (!string.Equals(existing.Name, displayName, StringComparison.Ordinal))
            {
                existing.Name = displayName;
                hasChanges = true;
            }

            if (!string.IsNullOrWhiteSpace(normalizedAvatar) && !string.Equals(existing.AvatarUrl, normalizedAvatar, StringComparison.Ordinal))
            {
                existing.AvatarUrl = normalizedAvatar;
                hasChanges = true;
            }

            if (string.IsNullOrWhiteSpace(existing.AvatarUrl))
            {
                existing.AvatarUrl = BuildDefaultAvatarUrl(displayName);
                hasChanges = true;
            }

            if (hasChanges)
            {
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }

            return existing;
        }

        var author = new Author
        {
            Name = displayName,
            NormalizedName = normalizedName,
            AvatarUrl = !string.IsNullOrWhiteSpace(normalizedAvatar)
                ? normalizedAvatar
                : BuildDefaultAvatarUrl(displayName),
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

    private static string? NormalizeAvatarUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        // Reject relative/local asset paths (e.g., /img/*.svg) to avoid storing placeholders
        if (Uri.TryCreate(trimmed, UriKind.Relative, out _))
        {
            return null;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return null;
        }

        // Treat local static assets as placeholders and ignore
        if (uri.AbsolutePath.StartsWith("/img/", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Normalize scheme to https
        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(uri)
            {
                Scheme = Uri.UriSchemeHttps,
                Port = -1
            };
            uri = builder.Uri;
        }

        var normalized = uri.ToString();
        if (normalized.Length > 500)
        {
            normalized = normalized[..500];
        }
        return normalized;
    }

    private static string BuildDefaultAvatarUrl(string name)
    {
        // Use a neutral local placeholder; Douban avatar will override after community refresh.
        return "/img/author-placeholder.svg";
    }
}
