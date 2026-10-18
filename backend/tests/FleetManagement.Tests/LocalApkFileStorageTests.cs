namespace FleetManagement.Tests;

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FleetManagement.Releases;
using Xunit;

public class LocalApkFileStorageTests
{
    private readonly string _testDirectory;

    public LocalApkFileStorageTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"apk-storage-test-{Guid.NewGuid()}");
    }

    [Fact]
    public void Constructor_CreatesBaseDirectory()
    {
        var storage = new LocalApkFileStorage(_testDirectory);
        Assert.True(Directory.Exists(_testDirectory));
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsStreamForExistingFile()
    {
        var releaseId = Guid.NewGuid().ToString();
        var filename = "test.apk";
        var testData = "APK file content"u8.ToArray();

        // Setup
        var storage = new LocalApkFileStorage(_testDirectory);
        var releaseDir = Path.Combine(_testDirectory, releaseId);
        Directory.CreateDirectory(releaseDir);
        var filePath = Path.Combine(releaseDir, filename);
        await File.WriteAllBytesAsync(filePath, testData);

        // Act
        using var stream = await storage.OpenReadAsync(releaseId, filename);

        // Assert
        Assert.NotNull(stream);
        Assert.True(stream.CanRead);
        Assert.True(stream.CanSeek);
        
        var buffer = new byte[testData.Length];
        var bytesRead = await stream.ReadAsync(buffer);
        Assert.Equal(testData.Length, bytesRead);
        Assert.Equal(testData, buffer);
    }

    [Fact]
    public async Task OpenReadAsync_ThrowsWhenFileNotFound()
    {
        var releaseId = Guid.NewGuid().ToString();
        var storage = new LocalApkFileStorage(_testDirectory);

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => storage.OpenReadAsync(releaseId, "nonexistent.apk")
        );
    }

    [Fact]
    public async Task OpenReadAsync_ThrowsWithInvalidReleaseId()
    {
        var storage = new LocalApkFileStorage(_testDirectory);

        await Assert.ThrowsAsync<ArgumentException>(
            () => storage.OpenReadAsync("invalid-id", "test.apk")
        );
    }

    [Fact]
    public async Task OpenReadAsync_ThrowsWithEmptyFilename()
    {
        var storage = new LocalApkFileStorage(_testDirectory);

        await Assert.ThrowsAsync<ArgumentException>(
            () => storage.OpenReadAsync(Guid.NewGuid().ToString(), "")
        );
    }

    [Fact]
    public async Task OpenReadAsync_PreventsDirectoryTraversal()
    {
        var releaseId = Guid.NewGuid().ToString();
        var storage = new LocalApkFileStorage(_testDirectory);

        // Try to access files outside the release directory
        await Assert.ThrowsAsync<ArgumentException>(
            () => storage.OpenReadAsync(releaseId, "../../../etc/passwd")
        );
    }

    [Fact]
    public async Task GetFileSizeAsync_ReturnsCorrectSize()
    {
        var releaseId = Guid.NewGuid().ToString();
        var filename = "test.apk";
        var testData = new byte[1024 * 10]; // 10 KB

        // Setup
        var storage = new LocalApkFileStorage(_testDirectory);
        var releaseDir = Path.Combine(_testDirectory, releaseId);
        Directory.CreateDirectory(releaseDir);
        var filePath = Path.Combine(releaseDir, filename);
        await File.WriteAllBytesAsync(filePath, testData);

        // Act
        var size = await storage.GetFileSizeAsync(releaseId, filename);

        // Assert
        Assert.Equal(testData.Length, size);
    }

    [Fact]
    public async Task GetFileSizeAsync_ThrowsWhenFileNotFound()
    {
        var releaseId = Guid.NewGuid().ToString();
        var storage = new LocalApkFileStorage(_testDirectory);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => storage.GetFileSizeAsync(releaseId, "nonexistent.apk")
        );
    }

    [Fact]
    public async Task FileExistsAsync_ReturnsTrueForExistingFile()
    {
        var releaseId = Guid.NewGuid().ToString();
        var filename = "test.apk";

        // Setup
        var storage = new LocalApkFileStorage(_testDirectory);
        var releaseDir = Path.Combine(_testDirectory, releaseId);
        Directory.CreateDirectory(releaseDir);
        var filePath = Path.Combine(releaseDir, filename);
        await File.WriteAllBytesAsync(filePath, new byte[] { 1, 2, 3 });

        // Act & Assert
        Assert.True(await storage.FileExistsAsync(releaseId, filename));
    }

    [Fact]
    public async Task FileExistsAsync_ReturnsFalseForMissingFile()
    {
        var releaseId = Guid.NewGuid().ToString();
        var storage = new LocalApkFileStorage(_testDirectory);

        // Act & Assert
        Assert.False(await storage.FileExistsAsync(releaseId, "nonexistent.apk"));
    }

    [Fact]
    public async Task FileExistsAsync_PreventsDirectoryTraversal()
    {
        var releaseId = Guid.NewGuid().ToString();
        var storage = new LocalApkFileStorage(_testDirectory);

        await Assert.ThrowsAsync<ArgumentException>(
            () => storage.FileExistsAsync(releaseId, "..\\..\\etc\\passwd")
        );
    }

    [Fact]
    public async Task MultipleReleases_AreStoredInSeparateDirectories()
    {
        var releaseId1 = Guid.NewGuid().ToString();
        var releaseId2 = Guid.NewGuid().ToString();
        var filename = "test.apk";

        // Setup
        var storage = new LocalApkFileStorage(_testDirectory);
        var releaseDir1 = Path.Combine(_testDirectory, releaseId1);
        var releaseDir2 = Path.Combine(_testDirectory, releaseId2);
        Directory.CreateDirectory(releaseDir1);
        Directory.CreateDirectory(releaseDir2);
        
        await File.WriteAllBytesAsync(Path.Combine(releaseDir1, filename), new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(releaseDir2, filename), new byte[] { 2 });

        // Act & Assert
        var data1 = await storage.OpenReadAsync(releaseId1, filename);
        var data2 = await storage.OpenReadAsync(releaseId2, filename);
        
        using (data1)
        using (data2)
        {
            Assert.Equal(1, data1.ReadByte());
            Assert.Equal(2, data2.ReadByte());
        }
    }

    [Fact]
    public void Constructor_ThrowsWithNullDirectory()
    {
        Assert.Throws<ArgumentNullException>(() => new LocalApkFileStorage(null!));
    }
}
