using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using System.IO;

namespace BookWise.Web.Controllers;

[ApiController]
[Route("cache/avatars")]
public class AvatarCacheController : ControllerBase
{
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly IContentTypeProvider _contentTypeProvider;

    public AvatarCacheController(IWebHostEnvironment webHostEnvironment, IContentTypeProvider contentTypeProvider)
    {
        _webHostEnvironment = webHostEnvironment;
        _contentTypeProvider = contentTypeProvider;
    }

    [HttpGet("{fileName}")]
    public IActionResult GetCachedAvatar(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains(".."))
        {
            return NotFound();
        }

        var cacheDirectory = Path.Combine(_webHostEnvironment.WebRootPath, "cache", "avatars");
        var filePath = Path.Combine(cacheDirectory, fileName);

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound();
        }

        // Determine content type
        if (!_contentTypeProvider.TryGetContentType(fileName, out var contentType))
        {
            contentType = "image/jpeg"; // Default fallback
        }

        // Set cache headers for better performance
        Response.Headers.CacheControl = "public, max-age=2592000"; // 30 days
        Response.Headers.ETag = $"\"{Path.GetFileName(filePath)}\"";

        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(fileStream, contentType);
    }
}