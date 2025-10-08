using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;

namespace BookWise.Web.Services.Caching;

public interface ICoverImageCacheService
{
    Task<CachedImageResult?> GetOrAddAsync(string? originalUrl, CancellationToken cancellationToken = default);
}

public sealed record CachedImageResult(string FilePath, string ContentType, DateTimeOffset LastModified);

public sealed class CoverImageCacheService : ICoverImageCacheService
{
    private const int MaxImageBytes = 5 * 1024 * 1024; // 5 MB guardrail

    private readonly HttpClient _httpClient;
    private readonly ILogger<CoverImageCacheService> _logger;
    private readonly IContentTypeProvider _contentTypeProvider;
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public CoverImageCacheService(
        HttpClient httpClient,
        ILogger<CoverImageCacheService> logger,
        IWebHostEnvironment environment,
        IContentTypeProvider contentTypeProvider)
    {
        _httpClient = httpClient;
        _logger = logger;
        _contentTypeProvider = contentTypeProvider;
        _cacheDirectory = Path.Combine(environment.WebRootPath, "cache", "covers");
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<CachedImageResult?> GetOrAddAsync(string? originalUrl, CancellationToken cancellationToken = default)
    {
        if (!TryValidateSource(originalUrl, out var uri))
        {
            return null;
        }

        var extension = GetImageExtensionFromUrl(uri);
        var fileName = GenerateFileName(originalUrl!, extension);
        var filePath = Path.Combine(_cacheDirectory, fileName);

        if (File.Exists(filePath))
        {
            var lastModified = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero);
            return new CachedImageResult(filePath, ResolveContentType(filePath), lastModified);
        }

        var fetchLock = _locks.GetOrAdd(fileName, _ => new SemaphoreSlim(1, 1));
        await fetchLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(filePath))
            {
                var cachedLastModified = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero);
                return new CachedImageResult(filePath, ResolveContentType(filePath), cachedLastModified);
            }

            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[CoverCache] Failed to download {Url}: {StatusCode}", uri, response.StatusCode);
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!IsValidImageType(contentType))
            {
                _logger.LogWarning("[CoverCache] Unsupported content type {ContentType} for {Url}", contentType, uri);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var memory = new MemoryStream();

            var buffer = new byte[81920];
            int bytesRead;
            long totalBytes = 0;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                totalBytes += bytesRead;
                if (totalBytes > MaxImageBytes)
                {
                    _logger.LogWarning("[CoverCache] Image at {Url} exceeded max size ({Bytes} bytes)", uri, totalBytes);
                    return null;
                }

                await memory.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }

            if (totalBytes == 0)
            {
                _logger.LogWarning("[CoverCache] Empty image stream for {Url}", uri);
                return null;
            }

            await File.WriteAllBytesAsync(filePath, memory.ToArray(), cancellationToken);

            var lastModified = DateTimeOffset.UtcNow;
            File.SetLastWriteTimeUtc(filePath, lastModified.UtcDateTime);

            var resolvedContentType = !string.IsNullOrWhiteSpace(contentType)
                ? contentType!
                : ResolveContentType(filePath);

            return new CachedImageResult(filePath, resolvedContentType, lastModified);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CoverCache] Error caching cover image from {Url}", uri);
            return null;
        }
        finally
        {
            fetchLock.Release();
            _locks.TryRemove(fileName, out _);
        }
    }

    private static bool TryValidateSource(string? originalUrl, out Uri uri)
    {
        uri = default!;
        if (string.IsNullOrWhiteSpace(originalUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var created))
        {
            return false;
        }

        if (!string.Equals(created.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(created.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        uri = created;
        return true;
    }

    private static string GenerateFileName(string originalUrl, string extension)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(originalUrl));
        var hashString = Convert.ToHexString(hash)[..20];
        return $"{hashString}{extension}";
    }

    private static string GetImageExtensionFromUrl(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath);
        return NormalizeExtension(extension);
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return ".jpg";
        }

        return extension.ToLowerInvariant() switch
        {
            ".jpeg" => ".jpg",
            ".png" => ".png",
            ".gif" => ".gif",
            ".webp" => ".webp",
            ".avif" => ".avif",
            _ => ".jpg"
        };
    }

    private static bool IsValidImageType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveContentType(string filePath)
    {
        if (_contentTypeProvider.TryGetContentType(filePath, out var contentType))
        {
            return contentType;
        }

        return "image/jpeg";
    }
}
