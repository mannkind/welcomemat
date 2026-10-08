using System;
using System.IO;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class ThumbnailServiceTests
{
    // Real and decodable, because the code under test decodes it.
    private static readonly byte[] TinyJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRof" +
        "Hh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAAB" +
        "AAAAAAAAAAAAAAAAAAAAAv/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AfwD/2Q==");

    private string ImageDir = "";

    [SetUp]
    public void SetUp()
    {
        this.ImageDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(this.ImageDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.ImageDir))
        {
            Directory.Delete(this.ImageDir, recursive: true);
        }
    }

    private string ThumbPath => Path.Combine(this.ImageDir, "thumb.jpg");

    private void CreatePhoto(string filename, byte[]? bytes = null) =>
        File.WriteAllBytes(Path.Combine(this.ImageDir, filename), bytes ?? [0x01, 0x02, 0x03]);

    [Test]
    public void SetThumbnail_DecodesAndReencodesSourceToThumbJpg()
    {
        this.CreatePhoto("001_abc.jpg", TinyJpeg);

        Assert.That(ThumbnailService.SetThumbnail(this.ImageDir, "001_abc.jpg"), Is.True);
        Assert.That(File.Exists(Path.Combine(this.ImageDir, "001_abc.jpg")), Is.True);

        using var thumbStream = File.OpenRead(this.ThumbPath);
        var format = Image.DetectFormat(thumbStream);
        Assert.That(format, Is.TypeOf<JpegFormat>());

        thumbStream.Position = 0;
        using var image = Image.Load(thumbStream);
        Assert.That(image.Width, Is.EqualTo(1));
        Assert.That(image.Height, Is.EqualTo(1));
    }

    [Test]
    public void SetThumbnail_OverwritesExistingThumb()
    {
        File.WriteAllBytes(this.ThumbPath, [0x00]);
        this.CreatePhoto("001_abc.jpg", TinyJpeg);

        Assert.That(ThumbnailService.SetThumbnail(this.ImageDir, "001_abc.jpg"), Is.True);

        using var image = Image.Load(this.ThumbPath);
        Assert.That(image.Width, Is.EqualTo(1));
    }

    [Test]
    public void SetThumbnail_MissingSource_ReturnsFalse()
    {
        Assert.That(ThumbnailService.SetThumbnail(this.ImageDir, "001_missing.jpg"), Is.False);
        Assert.That(File.Exists(this.ThumbPath), Is.False);
    }

    [Test]
    public void SetThumbnail_UndecodableSource_IsRejected_AndDoesNotWriteThumb()
    {
        // Right extension, garbage inside. The decode is what has to catch this.
        this.CreatePhoto("001_fake.jpg", [0x01, 0x02, 0x03]);

        Assert.That(ThumbnailService.SetThumbnail(this.ImageDir, "001_fake.jpg"), Is.False);
        Assert.That(File.Exists(this.ThumbPath), Is.False);
    }

    [Test]
    public void SetThumbnail_NonJpegContentNamedDotJpg_IsRejected()
    {
        // A PNG in a .jpg costume. The extension doesn't get a vote.
        byte[] pngBytes =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0,
        ];
        this.CreatePhoto("001_fake.jpg", pngBytes);

        Assert.That(ThumbnailService.SetThumbnail(this.ImageDir, "001_fake.jpg"), Is.False);
        Assert.That(File.Exists(this.ThumbPath), Is.False);
    }

    [TestCase("../evil.jpg")]
    [TestCase("..\\evil.jpg")]
    [TestCase("sub/dir.jpg")]
    public void SetThumbnail_RejectsPathSeparators(string filename)
    {
        Assert.That(ThumbnailService.SetThumbnail(this.ImageDir, filename), Is.False);
        Assert.That(File.Exists(this.ThumbPath), Is.False);
    }

    [Test]
    public void SetThumbnail_RejectsNonJpgExtension()
    {
        this.CreatePhoto("photo.png");
        Assert.That(ThumbnailService.SetThumbnail(this.ImageDir, "photo.png"), Is.False);
        Assert.That(File.Exists(this.ThumbPath), Is.False);
    }

    [TestCase("thumb.jpg")]
    [TestCase("Thumb.JPG")]
    public void SetThumbnail_RejectsThumbItself(string filename)
    {
        File.WriteAllBytes(this.ThumbPath, [0x00]);
        Assert.That(ThumbnailService.SetThumbnail(this.ImageDir, filename), Is.False);
    }

    [Test]
    public void RemoveThumbnail_DeletesThumb()
    {
        File.WriteAllBytes(this.ThumbPath, [0x00]);
        Assert.That(ThumbnailService.RemoveThumbnail(this.ImageDir), Is.True);
        Assert.That(File.Exists(this.ThumbPath), Is.False);
    }

    [Test]
    public void RemoveThumbnail_WhenMissing_ReturnsFalse() =>
        Assert.That(ThumbnailService.RemoveThumbnail(this.ImageDir), Is.False);

    [Test]
    public void RemoveThumbnail_WhenDirectoryMissing_ReturnsFalse() =>
        Assert.That(ThumbnailService.RemoveThumbnail(Path.Combine(this.ImageDir, "nope")), Is.False);

    [Test]
    public void HasThumbnail_TrueWhenPresent()
    {
        File.WriteAllBytes(this.ThumbPath, [0x00]);
        Assert.That(ThumbnailService.HasThumbnail(this.ImageDir), Is.True);
    }

    [Test]
    public void HasThumbnail_FalseWhenAbsent() =>
        Assert.That(ThumbnailService.HasThumbnail(this.ImageDir), Is.False);

    [Test]
    public void HasThumbnail_FalseWhenDirectoryMissing() =>
        Assert.That(ThumbnailService.HasThumbnail(Path.Combine(this.ImageDir, "nope")), Is.False);
}
