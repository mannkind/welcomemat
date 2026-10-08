using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing a way to pick and re-encode a property's thumbnail.
/// </summary>
public static class ThumbnailService
{
    /// <summary>
    /// The thumbnail's fixed name, so nothing has to record which photo it came from.
    /// </summary>
    public const string ThumbFileName = "thumb.jpg";

    /// <summary>
    /// The longest edge of a thumbnail.
    /// </summary>
    private const int THUMBNAIL_DIMENSION = 480;

    /// <summary>
    /// The encoder used internally.
    /// </summary>
    private static readonly JpegEncoder ThumbnailEncoder = new() { Quality = 80 };

    /// <summary>
    /// Re-encode a photo as the thumbnail; false when the filename is not one
    /// of this property's photos.
    /// </summary>
    /// <param name="imageDir"></param>
    /// <param name="filename"></param>
    /// <returns></returns>
    public static bool SetThumbnail(string imageDir, string filename)
    {
        if (filename.Contains('/') || filename.Contains('\\'))
        {
            return false;
        }

        if (!filename.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (filename.Equals(ThumbFileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var src = Path.Combine(imageDir, filename);
        if (!File.Exists(src))
        {
            return false;
        }

        try
        {
            using var image = Image.Load(src);
            image.Mutate(x => x.AutoOrient());
            if (image.Width > THUMBNAIL_DIMENSION || image.Height > THUMBNAIL_DIMENSION)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(THUMBNAIL_DIMENSION, THUMBNAIL_DIMENSION)
                }));
            }
            image.SaveAsJpeg(Path.Combine(imageDir, ThumbFileName), ThumbnailEncoder);
        }
        catch (UnknownImageFormatException)
        {
            return false;
        }
        catch (InvalidImageContentException)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Delete the thumbnail; false when there was none.
    /// </summary>
    /// <param name="imageDir"></param>
    /// <returns></returns>
    public static bool RemoveThumbnail(string imageDir)
    {
        var thumb = Path.Combine(imageDir, ThumbFileName);
        if (!File.Exists(thumb))
        {
            return false;
        }

        File.Delete(thumb);
        return true;
    }

    /// <summary>
    /// Determine if a thumbnail has been set.
    /// </summary>
    /// <param name="imageDir"></param>
    /// <returns></returns>
    public static bool HasThumbnail(string imageDir) =>
        File.Exists(Path.Combine(imageDir, ThumbFileName));
}
