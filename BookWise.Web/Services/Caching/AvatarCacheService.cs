using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace BookWise.Web.Services.Caching;

public interface IAvatarCacheService
{
    Task<string?> GetCachedAvatarUrlAsync(string originalUrl, CancellationToken cancellationToken = default);
    // Returns the cached URL if the file already exists, without attempting any network fetch
    Task<string?> TryGetCachedAvatarUrlAsync(string originalUrl);
    Task<bool> IsCachedAsync(string originalUrl);
    Task ClearCacheAsync();
}

public sealed class AvatarCacheService : IAvatarCacheService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AvatarCacheService> _logger;
    private readonly string _cacheDirectory;

    public AvatarCacheService(HttpClient httpClient, ILogger<AvatarCacheService> logger, IWebHostEnvironment webHostEnvironment)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cacheDirectory = Path.Combine(webHostEnvironment.WebRootPath, "cache", "avatars");
        
        // Ensure cache directory exists
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<string?> GetCachedAvatarUrlAsync(string originalUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(originalUrl))
        {
            return null;
        }

        // Generate a safe filename from the URL
        var fileName = GenerateFileName(originalUrl);
        var filePath = Path.Combine(_cacheDirectory, fileName);

        // Check if already cached
        if (File.Exists(filePath))
        {
            _logger.LogDebug("Avatar cache hit for {OriginalUrl}", originalUrl);
            return $"/cache/avatars/{fileName}";
        }

        // Download and cache the image
        try
        {
            _logger.LogInformation("Downloading avatar from {OriginalUrl}", originalUrl);
            
            using var response = await _httpClient.GetAsync(originalUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to download avatar from {OriginalUrl}: {StatusCode}", originalUrl, response.StatusCode);
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!IsValidImageType(contentType))
            {
                _logger.LogWarning("Invalid image type {ContentType} for avatar from {OriginalUrl}", contentType, originalUrl);
                return null;
            }

            var imageData = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (imageData.Length == 0)
            {
                _logger.LogWarning("Empty image data for avatar from {OriginalUrl}", originalUrl);
                return null;
            }

            // Write to cache
            await File.WriteAllBytesAsync(filePath, imageData, cancellationToken);
            _logger.LogInformation("Cached avatar from {OriginalUrl} to {FilePath}", originalUrl, filePath);

            return $"/cache/avatars/{fileName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching avatar from {OriginalUrl}", originalUrl);
            return null;
        }
    }

    public Task<string?> TryGetCachedAvatarUrlAsync(string originalUrl)
    {
        if (string.IsNullOrWhiteSpace(originalUrl))
        {
            return Task.FromResult<string?>(null);
        }

        var fileName = GenerateFileName(originalUrl);
        var filePath = Path.Combine(_cacheDirectory, fileName);
        if (File.Exists(filePath))
        {
            return Task.FromResult<string?>($"/cache/avatars/{fileName}");
        }

        return Task.FromResult<string?>(null);
    }

    public Task<bool> IsCachedAsync(string originalUrl)
    {
        if (string.IsNullOrWhiteSpace(originalUrl))
        {
            return Task.FromResult(false);
        }

        var fileName = GenerateFileName(originalUrl);
        var filePath = Path.Combine(_cacheDirectory, fileName);
        return Task.FromResult(File.Exists(filePath));
    }

    public Task ClearCacheAsync()
    {
        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                var files = Directory.GetFiles(_cacheDirectory);
                foreach (var file in files)
                {
                    File.Delete(file);
                }
                _logger.LogInformation("Cleared avatar cache, deleted {FileCount} files", files.Length);
            }
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing avatar cache");
            throw;
        }
    }

    private static string GenerateFileName(string url)
    {
        // Create a hash of the URL to generate a safe, unique filename
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(url));
        var hashString = Convert.ToHexString(hash)[..16]; // Use first 16 chars

        // Try to determine file extension from URL
        var extension = GetImageExtensionFromUrl(url);
        return $"{hashString}{extension}";
    }

    private static string GetImageExtensionFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;
            var extension = Path.GetExtension(path);
            
            return extension.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => ".jpg",
                ".png" => ".png",
                ".gif" => ".gif",
                ".webp" => ".webp",
                _ => ".jpg" // Default to jpg
            };
        }
        catch
        {
            return ".jpg"; // Default fallback
        }
    }

    private static bool IsValidImageType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }
}
