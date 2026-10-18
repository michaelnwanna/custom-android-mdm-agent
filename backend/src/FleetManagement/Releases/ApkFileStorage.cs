namespace FleetManagement.Releases;

using System;
using System.IO;
using System.Threading.Tasks;

/// <summary>
/// Abstracts APK file storage access, supporting various backends (local filesystem, S3, blob storage).
/// </summary>
public interface IApkFileStorage
{
    /// <summary>
    /// Opens a file stream for reading an APK.
    /// </summary>
    /// <param name="releaseId">Release ID (UUID)</param>
    /// <param name="filename">Filename (e.g., "com.example.app.apk")</param>
    /// <returns>Stream positioned at start. Caller must dispose.</returns>
    Task<Stream> OpenReadAsync(string releaseId, string filename);

    /// <summary>
    /// Gets the total size of an APK file.
    /// </summary>
    Task<long> GetFileSizeAsync(string releaseId, string filename);

    /// <summary>
    /// Checks if an APK file exists.
    /// </summary>
    Task<bool> FileExistsAsync(string releaseId, string filename);
}

/// <summary>
/// Local filesystem-based APK storage.
/// Stores APK files in a directory structure: {baseDirectory}/{releaseId}/{filename}
/// </summary>
public class LocalApkFileStorage : IApkFileStorage
{
    private readonly string _baseDirectory;

    /// <summary>
    /// Creates a new local file storage with the specified base directory.
    /// Directory will be created if it doesn't exist.
    /// </summary>
    /// <param name="baseDirectory">Root directory for APK files</param>
    public LocalApkFileStorage(string baseDirectory)
    {
        _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
        
        // Ensure directory exists
        Directory.CreateDirectory(_baseDirectory);
    }

    public Task<Stream> OpenReadAsync(string releaseId, string filename)
    {
        ValidateInputs(releaseId, filename);
        
        var filePath = GetFilePath(releaseId, filename);
        
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"APK file not found: {filePath}");
        
        // Return file stream opened for reading.
        // BufferSize of 64KB is optimal for streaming large files.
        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        return Task.FromResult<Stream>(stream);
    }

    public Task<long> GetFileSizeAsync(string releaseId, string filename)
    {
        ValidateInputs(releaseId, filename);
        
        var filePath = GetFilePath(releaseId, filename);
        
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"APK file not found: {filePath}");
        
        var fileInfo = new FileInfo(filePath);
        return Task.FromResult(fileInfo.Length);
    }

    public Task<bool> FileExistsAsync(string releaseId, string filename)
    {
        ValidateInputs(releaseId, filename);
        
        var filePath = GetFilePath(releaseId, filename);
        return Task.FromResult(File.Exists(filePath));
    }

    private string GetFilePath(string releaseId, string filename)
    {
        // Construct path and resolve to ensure it's within base directory (security check)
        var releaseDir = Path.Combine(_baseDirectory, releaseId);
        var fullPath = Path.Combine(releaseDir, filename);
        var resolvedPath = Path.GetFullPath(fullPath);
        var resolvedBase = Path.GetFullPath(_baseDirectory);

        // Prevent directory traversal attacks
        if (!resolvedPath.StartsWith(resolvedBase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path traversal attack detected");

        return resolvedPath;
    }

    private static void ValidateInputs(string releaseId, string filename)
    {
        if (string.IsNullOrWhiteSpace(releaseId) || !Guid.TryParse(releaseId, out _))
            throw new ArgumentException("Invalid release ID", nameof(releaseId));

        if (string.IsNullOrWhiteSpace(filename))
            throw new ArgumentException("Filename cannot be empty", nameof(filename));

        // Prevent directory traversal in filename
        if (filename.Contains("..") || filename.Contains("/") || filename.Contains("\\"))
            throw new ArgumentException("Invalid filename", nameof(filename));
    }
}
