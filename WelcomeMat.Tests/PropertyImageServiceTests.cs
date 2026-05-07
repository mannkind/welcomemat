using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using WelcomeMat.Config;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class PropertyImageServiceTests
{
    private static readonly byte[] TinyJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRof" +
        "Hh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAAB" +
        "AAAAAAAAAAAAAAAAAAAAAv/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AfwD/2Q==");

    private string StateDir = "";
    private PropertyImageService Service = null!;
    private Guid PropertyId;

    [SetUp]
    public void SetUp()
    {
        this.StateDir = Directory.CreateTempSubdirectory("welcomemat-images-").FullName;
        var opts = Options.Create(new AppOptions { StatePath = this.StateDir });
        this.Service = new PropertyImageService(opts, NullLogger<PropertyImageService>.Instance);
        this.PropertyId = Guid.NewGuid();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.StateDir))
        {
            Directory.Delete(this.StateDir, recursive: true);
        }
    }

    private string PropertyImageDir => Path.Combine(this.StateDir, "images", this.PropertyId.ToString());

    private static IFormFile MakeFile(byte[] bytes, string fileName = "photo.jpg", string contentType = "image/jpeg")
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "photos", fileName) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    [Test]
    public async Task SaveJpegsAsync_ValidJpeg_WritesGeneratedFilename()
    {
        var result = await this.Service.SaveJpegsAsync(this.PropertyId, [MakeFile(TinyJpeg)], default);

        Assert.That(result.Success, Is.True);
        var files = Directory.GetFiles(this.PropertyImageDir, "*.jpg");
        Assert.That(files, Has.Length.EqualTo(1));
        Assert.That(
            System.Text.RegularExpressions.Regex.IsMatch(
                Path.GetFileName(files[0]),
                @"^001_[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.jpg$"),
            Is.True);
    }

    [Test]
    public async Task SaveJpegsAsync_SecondUpload_IncrementsIndex()
    {
        await this.Service.SaveJpegsAsync(this.PropertyId, [MakeFile(TinyJpeg)], default);
        await this.Service.SaveJpegsAsync(this.PropertyId, [MakeFile(TinyJpeg)], default);

        var names = Directory.GetFiles(this.PropertyImageDir, "*.jpg").Select(Path.GetFileName).OrderBy(f => f).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(names[0], Does.StartWith("001_"));
            Assert.That(names[1], Does.StartWith("002_"));
        });
    }

    [Test]
    public async Task SaveJpegsAsync_OversizedFile_IsRejected_AndWritesNothing()
    {
        var oversized = new byte[10 * 1024 * 1024 + 1];
        TinyJpeg.CopyTo(oversized, 0);

        var result = await this.Service.SaveJpegsAsync(this.PropertyId, [MakeFile(oversized)], default);

        Assert.That(result.Success, Is.False);
        Assert.That(Directory.Exists(this.PropertyImageDir), Is.False);
    }

    [Test]
    public async Task SaveJpegsAsync_NonJpegNamedDotJpg_IsRejected()
    {
        var pngBytes = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0,
        };

        var result = await this.Service.SaveJpegsAsync(this.PropertyId, [MakeFile(pngBytes)], default);

        Assert.That(result.Success, Is.False);
        Assert.That(Directory.Exists(this.PropertyImageDir), Is.False);
    }

    [Test]
    public async Task SaveJpegsAsync_MoreThan20Files_IsRejected()
    {
        var files = Enumerable.Range(0, 21).Select(_ => MakeFile(TinyJpeg)).ToList();

        var result = await this.Service.SaveJpegsAsync(this.PropertyId, files, default);

        Assert.That(result.Success, Is.False);
        Assert.That(Directory.Exists(this.PropertyImageDir), Is.False);
    }

    [Test]
    public async Task SaveJpegsAsync_Exactly20Files_Succeeds()
    {
        var files = Enumerable.Range(0, 20).Select(_ => MakeFile(TinyJpeg)).ToList();

        var result = await this.Service.SaveJpegsAsync(this.PropertyId, files, default);

        Assert.That(result.Success, Is.True);
        Assert.That(Directory.GetFiles(this.PropertyImageDir, "*.jpg"), Has.Length.EqualTo(20));
    }

    [Test]
    public async Task SaveJpegsAsync_OneInvalidAmongMany_WritesNothing()
    {
        var files = new List<IFormFile> { MakeFile(TinyJpeg), MakeFile(TinyJpeg), MakeFile([1, 2, 3]) };

        var result = await this.Service.SaveJpegsAsync(this.PropertyId, files, default);

        Assert.That(result.Success, Is.False);
        Assert.That(!Directory.Exists(this.PropertyImageDir) || Directory.GetFiles(this.PropertyImageDir).Length == 0, Is.True);
    }

    [Test]
    public void OpenRead_ValidGeneratedFilename_ReturnsStream()
    {
        Directory.CreateDirectory(this.PropertyImageDir);
        var name = $"001_{Guid.NewGuid():D}.jpg";
        File.WriteAllBytes(Path.Combine(this.PropertyImageDir, name), TinyJpeg);

        using var stream = this.Service.OpenRead(this.PropertyId, name);

        Assert.That(stream, Is.Not.Null);
    }

    [Test]
    public void OpenRead_Thumb_ReturnsStream()
    {
        Directory.CreateDirectory(this.PropertyImageDir);
        File.WriteAllBytes(Path.Combine(this.PropertyImageDir, "thumb.jpg"), TinyJpeg);

        using var stream = this.Service.OpenRead(this.PropertyId, "thumb.jpg");

        Assert.That(stream, Is.Not.Null);
    }

    [TestCase("../evil.jpg")]
    [TestCase("..\\evil.jpg")]
    [TestCase("001_notaguid.jpg")]
    [TestCase("photo.jpg")]
    [TestCase("Thumb.jpg")]
    [TestCase("001_" + "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA" + ".jpg")]
    public void OpenRead_RejectsNonCanonicalFilenames(string filename)
    {
        Directory.CreateDirectory(this.PropertyImageDir);

        Assert.That(this.Service.OpenRead(this.PropertyId, filename), Is.Null);
    }

    [Test]
    public void OpenRead_MissingFile_ReturnsNull()
    {
        Directory.CreateDirectory(this.PropertyImageDir);

        Assert.That(this.Service.OpenRead(this.PropertyId, $"001_{Guid.NewGuid():D}.jpg"), Is.Null);
    }

    [Test]
    public void DeletePropertyDirectory_RemovesExactGuidDirectory()
    {
        Directory.CreateDirectory(this.PropertyImageDir);
        File.WriteAllBytes(Path.Combine(this.PropertyImageDir, "thumb.jpg"), TinyJpeg);

        this.Service.DeletePropertyDirectory(this.PropertyId);

        Assert.That(Directory.Exists(this.PropertyImageDir), Is.False);
    }

    [Test]
    public void DeletePropertyDirectory_MissingDirectory_DoesNotThrow() =>
        Assert.DoesNotThrow(() => this.Service.DeletePropertyDirectory(this.PropertyId));

    [Test]
    public async Task CleanupOrphansAsync_RemovesDirectoriesNotInDatabase()
    {
        var orphanId = Guid.NewGuid();
        var keptId = Guid.NewGuid();
        Directory.CreateDirectory(Path.Combine(this.StateDir, "images", orphanId.ToString()));
        Directory.CreateDirectory(Path.Combine(this.StateDir, "images", keptId.ToString()));

        using var sqlite = new SqliteTestDatabase();
        await sqlite.RunnerWithAllMigrations().RunAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(sqlite.ConnectionString).Options);
        db.Properties.Add(new Property { Id = keptId, Name = "Kept", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var removed = await this.Service.CleanupOrphansAsync(db, default);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(Directory.Exists(Path.Combine(this.StateDir, "images", orphanId.ToString())), Is.False);
            Assert.That(Directory.Exists(Path.Combine(this.StateDir, "images", keptId.ToString())), Is.True);
        });
    }

    [Test]
    public async Task CleanupOrphansAsync_CapsAt100DirectoriesPerRun()
    {
        for (var i = 0; i < 105; i++)
        {
            Directory.CreateDirectory(Path.Combine(this.StateDir, "images", Guid.NewGuid().ToString()));
        }

        using var sqlite = new SqliteTestDatabase();
        await sqlite.RunnerWithAllMigrations().RunAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(sqlite.ConnectionString).Options);

        var removed = await this.Service.CleanupOrphansAsync(db, default);

        Assert.That(removed, Is.EqualTo(100));
    }

    [Test]
    public async Task CleanupOrphansAsync_NoImagesDirectory_ReturnsZero()
    {
        using var sqlite = new SqliteTestDatabase();
        await sqlite.RunnerWithAllMigrations().RunAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(sqlite.ConnectionString).Options);

        var removed = await this.Service.CleanupOrphansAsync(db, default);

        Assert.That(removed, Is.EqualTo(0));
    }

    [Test]
    public void ListGalleryFilenames_ExcludesThumbnail_AndOrdersByName()
    {
        Directory.CreateDirectory(this.PropertyImageDir);
        File.WriteAllBytes(Path.Combine(this.PropertyImageDir, "thumb.jpg"), TinyJpeg);
        File.WriteAllBytes(Path.Combine(this.PropertyImageDir, "002_bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb.jpg"), TinyJpeg);
        File.WriteAllBytes(Path.Combine(this.PropertyImageDir, "001_aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa.jpg"), TinyJpeg);

        var names = this.Service.ListGalleryFilenames(this.PropertyId);

        Assert.That(names, Is.EqualTo(new[]
        {
            "001_aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa.jpg",
            "002_bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb.jpg",
        }));
    }

    [Test]
    public void ListGalleryFilenames_MissingDirectory_ReturnsEmpty() =>
        Assert.That(this.Service.ListGalleryFilenames(this.PropertyId), Is.Empty);
}
