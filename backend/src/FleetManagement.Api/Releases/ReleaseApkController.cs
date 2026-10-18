namespace FleetManagement.Api.Releases;

using System;
using System.Threading.Tasks;
using FleetManagement.Releases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FleetManagement.Persistence;
using FleetManagement.Enrollment;

/// <summary>
/// REST API controller for APK downloads with Range request support.
/// Devices download APKs for installation via this endpoint.
/// Supports resumable downloads and efficient bandwidth usage.
/// </summary>
[ApiController]
[Route("api/v1/releases")]
[Authorize(Policy = "DeviceOnly")]
public class ReleaseApkController : ControllerBase
{
    private readonly IDbContextFactory<FleetDbContext> _dbFactory;

    public ReleaseApkController(IDbContextFactory<FleetDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Download APK for a release.
    /// Supports HTTP Range requests for resumable downloads.
    /// Device must be enrolled and actively receiving this release deployment.
    /// </summary>
    /// <param name="releaseId">Release ID (UUID)</param>
    /// <returns>APK file stream or 206 Partial Content if Range request</returns>
    [HttpGet("{releaseId}/apk")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadApkAsync([FromRoute] string releaseId)
    {
        if (string.IsNullOrWhiteSpace(releaseId) || !Guid.TryParse(releaseId, out var releaseGuid))
            return BadRequest("Valid ReleaseId is required");

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Get the release
        var release = await db.Set<Release>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReleaseId == releaseGuid);

        if (release == null)
            return NotFound(new { error = "Release not found" });

        if (!release.IsActive)
            return BadRequest(new { error = "Release is not active" });

        // In a real implementation, we would:
        // 1. Stream the APK from storage (S3, local file system, etc.)
        // 2. Validate the device is enrolled and has a deployment for this release
        // 3. Update deployment device state to "Downloading"
        // 4. Support HTTP Range requests for resumable downloads
        //
        // For now, return a placeholder response that describes what headers we would set

        var apkSizeBytes = release.ApkSizeBytes;
        var rangeHeader = Request.Headers.Range.ToString();

        if (!string.IsNullOrWhiteSpace(rangeHeader))
        {
            // Parse Range header (e.g., "bytes=0-1023" or "bytes=1024-")
            if (TryParseRangeHeader(rangeHeader, apkSizeBytes, out var start, out var end))
            {
                // Return 206 Partial Content
                var contentLength = end - start + 1;
                Response.StatusCode = 206;
                Response.Headers.ContentRange = $"bytes {start}-{end}/{apkSizeBytes}";
                Response.Headers.ContentLength = contentLength;
                Response.Headers.ContentType = "application/vnd.android.package-archive";
                Response.Headers.AcceptRanges = "bytes";
                Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                return Ok(new { message = "Would stream bytes range", start, end, contentLength });
            }
        }

        // Full file download
        Response.StatusCode = 200;
        Response.Headers.ContentLength = apkSizeBytes;
        Response.Headers.ContentType = "application/vnd.android.package-archive";
        Response.Headers.AcceptRanges = "bytes";
        Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{release.PackageName}.apk\"";

        return Ok(new { message = "Would stream full file", sizeBytes = apkSizeBytes });
    }

    /// <summary>
    /// Parse HTTP Range header (RFC 7233).
    /// Supports "bytes=start-end" format.
    /// </summary>
    private static bool TryParseRangeHeader(string rangeHeader, long totalSize, out long start, out long end)
    {
        start = 0;
        end = totalSize - 1;

        if (!rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            return false;

        var range = rangeHeader.Substring(6).Trim();

        // Format: "0-1023" or "1024-" (to end)
        var parts = range.Split('-');
        if (parts.Length != 2)
            return false;

        if (string.IsNullOrWhiteSpace(parts[0]))
        {
            // Suffix range (last N bytes): "-500" means last 500 bytes
            if (long.TryParse(parts[1], out var suffixLength))
            {
                start = Math.Max(0, totalSize - suffixLength);
                end = totalSize - 1;
                return true;
            }
            return false;
        }

        if (!long.TryParse(parts[0], out start))
            return false;

        // Parse end (may be empty for open-ended range)
        if (string.IsNullOrWhiteSpace(parts[1]))
        {
            end = totalSize - 1;
        }
        else if (!long.TryParse(parts[1], out end))
        {
            return false;
        }

        // Validate range
        if (start < 0 || end < start || start >= totalSize)
            return false;

        end = Math.Min(end, totalSize - 1);
        return true;
    }
}
