using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using WelcomeMat.Config;
using WelcomeMat.Data;

namespace WelcomeMat.Services;

/// <summary>
/// The outcome of a photo upload.
/// </summary>
public record PhotoUploadResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public int SavedCount { get; init; }

    /// <summary>
    /// A successful upload.
    /// </summary>
    /// <param name="savedCount"></param>
    /// <returns></returns>
    public static PhotoUploadResult Ok(int savedCount) =>
        new() { Success = true, Error = null, SavedCount = savedCount };

    /// <summary>
    /// A refused upload, and why.
    /// </summary>
    /// <param name="error"></param>
    /// <returns></returns>
    public static PhotoUploadResult Fail(string error) =>
        new() { Success = false, Error = error, SavedCount = 0 };
}

/// <summary>
/// An class representing a managed way to store a property's photos on disk.
/// </summary>
public partial class PropertyImageService
{
    /// <summary>
    /// Initializes a new instance of the PropertyImageService class.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="logger"></param>
    public PropertyImageService(IOptions<AppOptions> options, ILogger<PropertyImageService> logger)
    {
        this.Options = options;
        this.Logger = logger;
    }

    public const int MaxFilesPerRequest = 20;
    public const long MaxFileSizeBytes = 10L * 1024 * 1024; // 10 MiB
    private const int MAX_ORPHAN_DIRECTORIES_PER_RUN = 100;

    private const int MAX_GALLERY_DIMENSION = 2400;
    private static readonly JpegEncoder GalleryEncoder = new() { Quality = 85 };

    private string ImagesRoot => this.Options.Value.ImagesPath;

    [GeneratedRegex(@"^(?<index>\d{3})_(?<guid>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\.jpg$")]

    /// <summary>
    /// The names this service generates; anything else is not served.
    /// </summary>
    /// <returns></returns>
    private static partial Regex GeneratedFileNamePattern();

    /// <summary>
    /// Determine if the name is one this service generated; nothing else is served.
    /// </summary>
    /// <param name="filename"></param>
    /// <returns></returns>
    public static bool IsValidFilename(string filename)
    {
        if (filename == ThumbnailService.ThumbFileName)
        {
            return true;
        }

        var match = GeneratedFileNamePattern().Match(filename);
        if (!match.Success)
        {
            return false;
        }

        var guidText = match.Groups["guid"].Value;
        return Guid.TryParse(guidText, out var guid) && guid.ToString("D") == guidText;
    }

    /// <summary>
    /// Re-encode and store the uploaded photos, rejecting anything that will not
    /// decode as a JPEG.
    /// </summary>
    /// <param name="propertyId"></param>
    /// <param name="files"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<PhotoUploadResult> SaveJpegsAsync(
        Guid propertyId, IReadOnlyList<IFormFile> files, CancellationToken ct = default)
    {
        if (files.Count == 0)
        {
            return PhotoUploadResult.Ok(0);
        }

        if (files.Count > MaxFilesPerRequest)
        {
            return PhotoUploadResult.Fail($"No more than {MaxFilesPerRequest} photos may be uploaded at once.");
        }

        var stagingDir = Directory.CreateTempSubdirectory("welcomemat-upload-").FullName;
        try
        {
            var staged = new List<string>();
            foreach (var file in files)
            {
                if (file.Length <= 0 || file.Length > MaxFileSizeBytes)
                {
                    return PhotoUploadResult.Fail(
                        $"Each photo must be a non-empty JPEG no larger than {MaxFileSizeBytes / (1024 * 1024)} MiB.");
                }

                var tempPath = Path.Combine(stagingDir, Guid.NewGuid().ToString("N"));
                await using (var input = file.OpenReadStream())
                await using (var output = File.Create(tempPath))
                {
                    await input.CopyToAsync(output, ct);
                }

                if (!await IsDecodableJpegAsync(tempPath, ct))
                {
                    return PhotoUploadResult.Fail("Each photo must be a valid JPEG image.");
                }

                staged.Add(tempPath);
            }

            var imageDir = Path.Combine(this.ImagesRoot, propertyId.ToString("D"));
            Directory.CreateDirectory(imageDir);
            var nextIndex = NextIndex(imageDir);

            foreach (var tempPath in staged)
            {
                var dest = Path.Combine(imageDir, $"{++nextIndex:D3}_{Guid.NewGuid():D}.jpg");
                using var image = await Image.LoadAsync(tempPath, ct);
                image.Mutate(x => x.AutoOrient());
                if (image.Width > MAX_GALLERY_DIMENSION || image.Height > MAX_GALLERY_DIMENSION)
                {
                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(MAX_GALLERY_DIMENSION, MAX_GALLERY_DIMENSION)
                    }));
                }
                await image.SaveAsJpegAsync(dest, GalleryEncoder, ct);
            }

            return PhotoUploadResult.Ok(staged.Count);
        }
        finally
        {
            try { Directory.Delete(stagingDir, recursive: true); }
            catch (IOException) { /* the OS will get it eventually; not worth failing an upload over */ }
        }
    }

    /// <summary>
    /// Open a stored photo; null when it is not there.
    /// </summary>
    /// <param name="propertyId"></param>
    /// <param name="filename"></param>
    /// <returns></returns>
    public Stream? OpenRead(Guid propertyId, string filename)
    {
        if (!IsValidFilename(filename))
        {
            return null;
        }

        var dir = Path.GetFullPath(Path.Combine(this.ImagesRoot, propertyId.ToString("D")));
        var path = Path.GetFullPath(Path.Combine(dir, filename));

        if (!string.Equals(Path.GetDirectoryName(path), dir, StringComparison.Ordinal))
        {
            return null;
        }

        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    /// <summary>
    /// The property's photos, in display order.
    /// </summary>
    /// <param name="propertyId"></param>
    /// <returns></returns>
    public IReadOnlyList<string> ListGalleryFilenames(Guid propertyId)
    {
        var dir = Path.Combine(this.ImagesRoot, propertyId.ToString("D"));
        if (!Directory.Exists(dir))
        {
            return [];
        }

        return Directory.GetFiles(dir, "*.jpg")
            .Select(Path.GetFileName)
            .Where(f => f is not null && !f.Equals(ThumbnailService.ThumbFileName, StringComparison.OrdinalIgnoreCase))
            .Select(f => f!)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Delete every photo for a property.
    /// </summary>
    /// <param name="propertyId"></param>
    public void DeletePropertyDirectory(Guid propertyId)
    {
        var dir = Path.Combine(this.ImagesRoot, propertyId.ToString("D"));
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Delete photo directories whose property is gone, a batch at a time.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<int> CleanupOrphansAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (!Directory.Exists(this.ImagesRoot))
        {
            return 0;
        }

        var existingIds = (await db.Properties.Select(p => p.Id).ToListAsync(ct)).ToHashSet();
        var removed = 0;

        foreach (var dir in Directory.EnumerateDirectories(this.ImagesRoot))
        {
            if (removed >= MAX_ORPHAN_DIRECTORIES_PER_RUN)
            {
                break;
            }

            var name = Path.GetFileName(dir);
            if (!Guid.TryParse(name, out var id) || existingIds.Contains(id))
            {
                continue;
            }

            try
            {
                Directory.Delete(dir, recursive: true);
                removed++;
            }
            catch (IOException ex)
            {
                this.Logger.LogWarning(ex, "Could not remove orphaned property image directory {Directory}.", dir);
            }
        }

        return removed;
    }

    private static int NextIndex(string imageDir) =>
        Directory.GetFiles(imageDir, "*.jpg")
            .Select(f => Path.GetFileNameWithoutExtension(Path.GetFileName(f)))
            .Select(n => { var parts = n.Split('_', 2); return parts.Length == 2 && int.TryParse(parts[0], out var idx) ? idx : 0; })
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>
    /// Determine if the file really decodes as a JPEG.
    /// </summary>
    /// <param name="path"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    private static async Task<bool> IsDecodableJpegAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var format = await Image.DetectFormatAsync(stream, ct);
            if (format is not JpegFormat)
            {
                return false;
            }

            stream.Position = 0;
            using var image = await Image.LoadAsync(stream, ct);
            return image.Width > 0 && image.Height > 0;
        }
        catch (UnknownImageFormatException)
        {
            return false;
        }
        catch (InvalidImageContentException)
        {
            return false;
        }
    }

    /// <summary>
    /// The application options.
    /// </summary>
    private readonly IOptions<AppOptions> Options;

    /// <summary>
    /// The logger used internally.
    /// </summary>
    private readonly ILogger<PropertyImageService> Logger;
}
