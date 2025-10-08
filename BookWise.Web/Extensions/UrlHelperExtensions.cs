using Microsoft.AspNetCore.Mvc;

namespace BookWise.Web.Extensions;

public static class UrlHelperExtensions
{
    public static string ResolveCoverImage(this IUrlHelper urlHelper, string? source)
    {
        ArgumentNullException.ThrowIfNull(urlHelper);

        if (string.IsNullOrWhiteSpace(source))
        {
            return urlHelper.Content("~/img/book-placeholder.svg");
        }

        if (source.StartsWith("~/", StringComparison.Ordinal) ||
            source.StartsWith("/", StringComparison.Ordinal))
        {
            return urlHelper.Content(source);
        }

        if (source.Contains("/api/images/cover", StringComparison.OrdinalIgnoreCase))
        {
            return urlHelper.Content(source);
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var absolute))
        {
            var scheme = absolute.Scheme;
            if (!string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            {
                return urlHelper.Content("~/img/book-placeholder.svg");
            }

            var encoded = Uri.EscapeDataString(absolute.AbsoluteUri);
            return urlHelper.Content($"~/api/images/cover?src={encoded}");
        }

        var sanitized = source.Trim();
        return urlHelper.Content($"~/{sanitized.TrimStart('/')}");
    }
}
